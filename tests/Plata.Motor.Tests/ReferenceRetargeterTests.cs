// SPDX-License-Identifier: MIT
using Mono.Cecil;
using Plata.Motor.Puente;
using Xunit;

public class ReferenceRetargeterTests
{
    // Construye un ensamblado mínimo que declara una referencia a otro ensamblado.
    private static byte[] EnsambladoConReferencia(string refName, Version ver)
    {
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Prueba", new Version(1, 0, 0, 0)), "Prueba", ModuleKind.Dll);
        asm.MainModule.AssemblyReferences.Add(new AssemblyNameReference(refName, ver));
        using var ms = new MemoryStream();
        asm.Write(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Traducir_referenciaDeSilverlight_seReescribe()
    {
        var bytes = EnsambladoConReferencia("System.Windows", new Version(2, 0, 5, 0));
        var salida = new ReferenceRetargeter().Traducir(bytes, out var reescritas);

        using var mod = ModuleDefinition.ReadModule(new MemoryStream(salida));
        Assert.Contains(mod.AssemblyReferences, a => a.Name == "OpenSilver");
        Assert.DoesNotContain(mod.AssemblyReferences, a => a.Name == "System.Windows");
        Assert.Contains("System.Windows", reescritas);
    }

    [Fact]
    public void Traducir_sinReferenciasDeSilverlight_quedaIntacto()
    {
        var bytes = EnsambladoConReferencia("System.Linq", new Version(10, 0, 0, 0));
        var salida = new ReferenceRetargeter().Traducir(bytes, out var reescritas);

        using var mod = ModuleDefinition.ReadModule(new MemoryStream(salida));
        Assert.Contains(mod.AssemblyReferences, a => a.Name == "System.Linq");
        Assert.Empty(reescritas);
    }

    [Fact]
    public void Traducir_variasAOpenSilver_noDuplicaReferencia()
    {
        // Un ensamblado que referencia dos librerías de UI de Silverlight: ambas van a OpenSilver.
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Prueba", new Version(1, 0, 0, 0)), "Prueba", ModuleKind.Dll);
        asm.MainModule.AssemblyReferences.Add(new AssemblyNameReference("System.Windows", new Version(2, 0, 5, 0)));
        asm.MainModule.AssemblyReferences.Add(new AssemblyNameReference("System.Windows.Controls", new Version(2, 0, 5, 0)));
        using var ms = new MemoryStream();
        asm.Write(ms);

        var salida = new ReferenceRetargeter().Traducir(ms.ToArray(), out _);
        using var mod = ModuleDefinition.ReadModule(new MemoryStream(salida));
        Assert.Equal(1, mod.AssemblyReferences.Count(a => a.Name == "OpenSilver"));
    }

    [Fact]
    public void Traducir_tipoDeBclEnEnsambladoNoCargado_seReubica()
    {
        // DataContractJsonSerializer: en Silverlight vive en System.ServiceModel.Web; en .NET
        // moderno, en otro ensamblado que el proceso todavía no cargó.
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Prueba", new Version(1, 0, 0, 0)), "Prueba", ModuleKind.Dll);
        var mod = asm.MainModule;
        var web = new AssemblyNameReference("System.ServiceModel.Web", new Version(2, 0, 5, 0));
        mod.AssemblyReferences.Add(web);
        var dcjs = new TypeReference("System.Runtime.Serialization.Json", "DataContractJsonSerializer", mod, web);
        var campo = new FieldDefinition("s", FieldAttributes.Public, mod.ImportReference(dcjs));
        var tipo = new TypeDefinition("Prueba", "Usa", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        tipo.Fields.Add(campo);
        mod.Types.Add(tipo);
        using var ms = new MemoryStream();
        asm.Write(ms);

        var salida = new ReferenceRetargeter().Traducir(ms.ToArray(), out _);

        using var traducido = ModuleDefinition.ReadModule(new MemoryStream(salida));
        var t = Assert.Single(traducido.GetTypeReferences(), r => r.Name == "DataContractJsonSerializer");
        Assert.Equal(EnsambladoDeDcjs(), ((AssemblyNameReference)t.Scope).Name);
    }

    // Aparte y sin inlining: si no, el JIT carga el ensamblado antes de traducir y la
    // prueba no ejercita la carga bajo demanda.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static string? EnsambladoDeDcjs()
        => typeof(System.Runtime.Serialization.Json.DataContractJsonSerializer).Assembly.GetName().Name;

    [Fact]
    public void Traducir_basicHttpBinding_vaAlClienteWcfDeNet()
    {
        // OpenSilver no trae el cliente WCF clásico; lo aporta System.ServiceModel.Http.
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Prueba", new Version(1, 0, 0, 0)), "Prueba", ModuleKind.Dll);
        var mod = asm.MainModule;
        var sm = new AssemblyNameReference("System.ServiceModel", new Version(2, 0, 5, 0));
        mod.AssemblyReferences.Add(sm);
        var binding = new TypeReference("System.ServiceModel", "BasicHttpBinding", mod, sm);
        var tipo = new TypeDefinition("Prueba", "Usa", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        tipo.Fields.Add(new FieldDefinition("b", FieldAttributes.Public, mod.ImportReference(binding)));
        mod.Types.Add(tipo);
        using var ms = new MemoryStream();
        asm.Write(ms);

        var salida = new ReferenceRetargeter().Traducir(ms.ToArray(), out _);

        using var traducido = ModuleDefinition.ReadModule(new MemoryStream(salida));
        var t = Assert.Single(traducido.GetTypeReferences(), r => r.Name == "BasicHttpBinding");
        Assert.Equal("System.ServiceModel.Http", ((AssemblyNameReference)t.Scope).Name);
    }

    [Fact]
    public void Traducir_createChannelGenerico_seDesviaConSuArgumento()
    {
        // class Despachante<TService> { TService Abrir(ChannelFactory<TService> f) => f.CreateChannel(); }
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Prueba", new Version(1, 0, 0, 0)), "Prueba", ModuleKind.Dll);
        var mod = asm.MainModule;
        var sm = new AssemblyNameReference("System.ServiceModel", new Version(2, 0, 5, 0));
        mod.AssemblyReferences.Add(sm);
        var tipo = new TypeDefinition("Prueba", "Despachante`1", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        var tService = new GenericParameter("TService", tipo);
        tipo.GenericParameters.Add(tService);
        mod.Types.Add(tipo);

        var cfAbierta = new TypeReference("System.ServiceModel", "ChannelFactory`1", mod, sm);
        cfAbierta.GenericParameters.Add(new GenericParameter("TChannel", cfAbierta));
        var cfDeTService = new GenericInstanceType(cfAbierta);
        cfDeTService.GenericArguments.Add(tService);
        var createChannel = new MethodReference("CreateChannel", cfAbierta.GenericParameters[0], cfDeTService) { HasThis = true };

        var metodo = new MethodDefinition("Abrir", MethodAttributes.Public, tService);
        metodo.Parameters.Add(new ParameterDefinition(cfDeTService));
        var il = metodo.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
        il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, createChannel);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        tipo.Methods.Add(metodo);
        using var ms = new MemoryStream();
        asm.Write(ms);

        var salida = new ReferenceRetargeter().Traducir(ms.ToArray(), out _);

        using var traducido = ModuleDefinition.ReadModule(new MemoryStream(salida));
        var llamada = traducido.GetType("Prueba.Despachante`1").Methods.Single().Body.Instructions
            .Single(i => i.Operand is MethodReference);
        var destino = Assert.IsType<GenericInstanceMethod>(llamada.Operand);
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Call, llamada.OpCode);
        Assert.Equal("Plata.Compat.Desvios", destino.DeclaringType.FullName);
        Assert.Equal(nameof(Plata.Compat.Desvios.CrearCanal), destino.Name);
        Assert.Equal("TService", Assert.Single(destino.GenericArguments).Name);
    }

    [Fact]
    public void Traducir_newChannelFactory_seDesviaANuevaFabrica()
    {
        // class Despachante<TService> { object Crear(Binding b, EndpointAddress a) => new ChannelFactory<TService>(b, a); }
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Prueba", new Version(1, 0, 0, 0)), "Prueba", ModuleKind.Dll);
        var mod = asm.MainModule;
        var sm = new AssemblyNameReference("System.ServiceModel", new Version(2, 0, 5, 0));
        mod.AssemblyReferences.Add(sm);
        var tipo = new TypeDefinition("Prueba", "Despachante`1", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        var tService = new GenericParameter("TService", tipo);
        tipo.GenericParameters.Add(tService);
        mod.Types.Add(tipo);

        var binding = new TypeReference("System.ServiceModel.Channels", "Binding", mod, sm);
        var direccion = new TypeReference("System.ServiceModel", "EndpointAddress", mod, sm);
        var cfAbierta = new TypeReference("System.ServiceModel", "ChannelFactory`1", mod, sm);
        cfAbierta.GenericParameters.Add(new GenericParameter("TChannel", cfAbierta));
        var cfDeTService = new GenericInstanceType(cfAbierta);
        cfDeTService.GenericArguments.Add(tService);
        var ctor = new MethodReference(".ctor", mod.TypeSystem.Void, cfDeTService) { HasThis = true };
        ctor.Parameters.Add(new ParameterDefinition(binding));
        ctor.Parameters.Add(new ParameterDefinition(direccion));

        var metodo = new MethodDefinition("Crear", MethodAttributes.Public, mod.TypeSystem.Object);
        metodo.Parameters.Add(new ParameterDefinition(binding));
        metodo.Parameters.Add(new ParameterDefinition(direccion));
        var il = metodo.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2);
        il.Emit(Mono.Cecil.Cil.OpCodes.Newobj, ctor);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        tipo.Methods.Add(metodo);
        using var ms = new MemoryStream();
        asm.Write(ms);

        var salida = new ReferenceRetargeter().Traducir(ms.ToArray(), out _);

        using var traducido = ModuleDefinition.ReadModule(new MemoryStream(salida));
        var llamada = traducido.GetType("Prueba.Despachante`1").Methods.Single().Body.Instructions
            .Single(i => i.Operand is MethodReference);
        var destino = Assert.IsType<GenericInstanceMethod>(llamada.Operand);
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Call, llamada.OpCode);
        Assert.Equal(nameof(Plata.Compat.Desvios.NuevaFabrica), destino.Name);
        Assert.Equal(2, destino.Parameters.Count);
        Assert.Equal("ChannelFactory`1", destino.ElementMethod.ReturnType.Name);
    }

    [Fact]
    public void Traducir_readObjectDeJson_seDesviaAPlata()
    {
        // callvirt DataContractJsonSerializer::ReadObject(Stream), compilado contra Silverlight.
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Prueba", new Version(1, 0, 0, 0)), "Prueba", ModuleKind.Dll);
        var mod = asm.MainModule;
        var web = new AssemblyNameReference("System.ServiceModel.Web", new Version(2, 0, 5, 0));
        var mscorlib = new AssemblyNameReference("mscorlib", new Version(2, 0, 5, 0));
        mod.AssemblyReferences.Add(web);
        mod.AssemblyReferences.Add(mscorlib);
        var dcjs = new TypeReference("System.Runtime.Serialization.Json", "DataContractJsonSerializer", mod, web);
        var stream = new TypeReference("System.IO", "Stream", mod, mscorlib);
        var readObject = new MethodReference("ReadObject", mod.TypeSystem.Object, dcjs) { HasThis = true };
        readObject.Parameters.Add(new ParameterDefinition(stream));

        var tipo = new TypeDefinition("Prueba", "Usa", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        var metodo = new MethodDefinition("Leer", MethodAttributes.Public | MethodAttributes.Static, mod.TypeSystem.Object);
        metodo.Parameters.Add(new ParameterDefinition(dcjs));
        metodo.Parameters.Add(new ParameterDefinition(stream));
        var il = metodo.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
        il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, readObject);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        tipo.Methods.Add(metodo);
        mod.Types.Add(tipo);
        using var ms = new MemoryStream();
        asm.Write(ms);

        var salida = new ReferenceRetargeter().Traducir(ms.ToArray(), out _);

        using var traducido = ModuleDefinition.ReadModule(new MemoryStream(salida));
        var llamada = traducido.GetType("Prueba.Usa").Methods.Single().Body.Instructions
            .Single(i => i.Operand is MethodReference);
        var destino = (MethodReference)llamada.Operand;
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Call, llamada.OpCode);
        Assert.Equal("Plata.Compat.Desvios", destino.DeclaringType.FullName);
        Assert.Equal(nameof(Plata.Compat.Desvios.LeerJsonDesdeElPrincipio), destino.Name);
        Assert.Equal(2, destino.Parameters.Count);
    }

    [Fact]
    public void Traducir_loadComponent_seDesviaAlCargadorDeXaml()
    {
        // call Application::LoadComponent(object, Uri), estático, contra Silverlight.
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Prueba", new Version(1, 0, 0, 0)), "Prueba", ModuleKind.Dll);
        var mod = asm.MainModule;
        var sw = new AssemblyNameReference("System.Windows", new Version(2, 0, 5, 0));
        var system = new AssemblyNameReference("System", new Version(2, 0, 5, 0));
        mod.AssemblyReferences.Add(sw);
        mod.AssemblyReferences.Add(system);
        var app = new TypeReference("System.Windows", "Application", mod, sw);
        var uri = new TypeReference("System", "Uri", mod, system);
        var load = new MethodReference("LoadComponent", mod.TypeSystem.Void, app) { HasThis = false };
        load.Parameters.Add(new ParameterDefinition(mod.TypeSystem.Object));
        load.Parameters.Add(new ParameterDefinition(uri));

        var tipo = new TypeDefinition("Prueba", "Vista", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        var metodo = new MethodDefinition("InitializeComponent", MethodAttributes.Public, mod.TypeSystem.Void);
        metodo.Parameters.Add(new ParameterDefinition(uri));
        var il = metodo.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, load);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        tipo.Methods.Add(metodo);
        mod.Types.Add(tipo);
        using var ms = new MemoryStream();
        asm.Write(ms);

        var salida = new ReferenceRetargeter().Traducir(ms.ToArray(), out _);

        using var traducido = ModuleDefinition.ReadModule(new MemoryStream(salida));
        var destino = (MethodReference)traducido.GetType("Prueba.Vista").Methods.Single().Body.Instructions
            .Single(i => i.Operand is MethodReference).Operand;
        Assert.Equal("Plata.Compat.Desvios", destino.DeclaringType.FullName);
        Assert.Equal(nameof(Plata.Compat.Desvios.CargarComponente), destino.Name);
        Assert.Equal(2, destino.Parameters.Count);
    }

    [Fact]
    public void Traducir_silverlightHost_esElHostDeOpenSilver()
    {
        // Application.Host devuelve SilverlightHost en Silverlight y System.Windows.Host en OpenSilver.
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Prueba", new Version(1, 0, 0, 0)), "Prueba", ModuleKind.Dll);
        var mod = asm.MainModule;
        var sw = new AssemblyNameReference("System.Windows", new Version(2, 0, 5, 0));
        mod.AssemblyReferences.Add(sw);
        var host = new TypeReference("System.Windows.Interop", "SilverlightHost", mod, sw);
        var tipo = new TypeDefinition("Prueba", "Usa", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        tipo.Fields.Add(new FieldDefinition("h", FieldAttributes.Public, mod.ImportReference(host)));
        mod.Types.Add(tipo);
        using var ms = new MemoryStream();
        asm.Write(ms);

        var salida = new ReferenceRetargeter().Traducir(ms.ToArray(), out _);

        using var traducido = ModuleDefinition.ReadModule(new MemoryStream(salida));
        var t = Assert.Single(traducido.GetTypeReferences(), r => r.Name is "Host" or "SilverlightHost");
        Assert.Equal("System.Windows.Host", t.FullName);
        Assert.Equal("OpenSilver", ((AssemblyNameReference)t.Scope).Name);
    }

    [Fact]
    public void Traducir_tipoEnArgumentoDeAtributo_seReubica()
    {
        // [TemplatePart(Name = "x", Type = typeof(ContentPresenter))]: el tipo va como texto
        // ("System.Windows.Controls.ContentPresenter, System.Windows, Version=2.0.5.0, ...").
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Prueba", new Version(1, 0, 0, 0)), "Prueba", ModuleKind.Dll);
        var mod = asm.MainModule;
        var sw = new AssemblyNameReference("System.Windows", new Version(2, 0, 5, 0))
        {
            PublicKeyToken = new byte[] { 0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e },
        };
        mod.AssemblyReferences.Add(sw);
        var atributo = new TypeReference("System.Windows", "TemplatePartAttribute", mod, sw);
        var ctor = new MethodReference(".ctor", mod.TypeSystem.Void, atributo) { HasThis = true };
        var presenter = new TypeReference("System.Windows.Controls", "ContentPresenter", mod, sw);
        var ca = new CustomAttribute(ctor);
        ca.Properties.Add(new CustomAttributeNamedArgument("Type",
            new CustomAttributeArgument(mod.ImportReference(typeof(Type)), presenter)));
        var tipo = new TypeDefinition("Prueba", "Control", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        tipo.CustomAttributes.Add(ca);
        mod.Types.Add(tipo);
        using var ms = new MemoryStream();
        asm.Write(ms);

        var salida = new ReferenceRetargeter().Traducir(ms.ToArray(), out _);

        using var traducido = ModuleDefinition.ReadModule(new MemoryStream(salida));
        var arg = traducido.GetType("Prueba.Control").CustomAttributes.Single().Properties.Single().Argument;
        var valor = Assert.IsAssignableFrom<TypeReference>(arg.Value);
        Assert.Equal("System.Windows.Controls.ContentPresenter", valor.FullName);
        Assert.Equal("OpenSilver", ((AssemblyNameReference)valor.Scope).Name);
    }

    [Fact]
    public void Traducir_aesManagedDeSilverlight_seReemplazaPorElDePlata()
    {
        // Un método que hace "new AesManaged()" contra el System.Core de Silverlight.
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Prueba", new Version(1, 0, 0, 0)), "Prueba", ModuleKind.Dll);
        var mod = asm.MainModule;
        var systemCore = new AssemblyNameReference("System.Core", new Version(2, 0, 5, 0));
        mod.AssemblyReferences.Add(systemCore);
        var aesRef = new TypeReference("System.Security.Cryptography", "AesManaged", mod, systemCore);
        var ctor = new MethodReference(".ctor", mod.TypeSystem.Void, aesRef) { HasThis = true };

        var tipo = new TypeDefinition("Prueba", "Usa", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        var metodo = new MethodDefinition("Crear", MethodAttributes.Public | MethodAttributes.Static, mod.TypeSystem.Object);
        var il = metodo.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Newobj, mod.ImportReference(ctor));
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        tipo.Methods.Add(metodo);
        mod.Types.Add(tipo);
        using var ms = new MemoryStream();
        asm.Write(ms);

        var salida = new ReferenceRetargeter().Traducir(ms.ToArray(), out _);

        using var traducido = ModuleDefinition.ReadModule(new MemoryStream(salida));
        var aes = Assert.Single(traducido.GetTypeReferences(), t => t.Name == "AesManaged");
        Assert.Equal("Plata.Compat", aes.Namespace);
        Assert.Equal("Plata.Compat", ((AssemblyNameReference)aes.Scope).Name);
    }
}
