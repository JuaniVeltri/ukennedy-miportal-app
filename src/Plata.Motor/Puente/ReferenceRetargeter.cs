// SPDX-License-Identifier: MIT
using Mono.Cecil;
using Mono.Cecil.Cil;
using AssemblyName = System.Reflection.AssemblyName;

namespace Plata.Motor.Puente;

/// <summary>
/// Reescribe, en memoria, una librería compilada para Silverlight para que corra
/// sobre OpenSilver y la BCL moderna:
///  - referencias a ensamblados de UI de Silverlight → OpenSilver;
///  - referencias de tipo de la BCL de Silverlight (System, System.Core, …) →
///    el ensamblado moderno de .NET que realmente contiene cada tipo.
/// </summary>
public sealed class ReferenceRetargeter
{
    private static readonly Version OpenSilverVersion = new(3, 3, 3, 0);

    // BCL de Silverlight cuyos tipos hay que reubicar en .NET moderno (sin mscorlib:
    // su fachada moderna ya reenvía los tipos del núcleo por nombre).
    private static readonly HashSet<string> BclAUnificar = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "System.Core", "System.Xml", "System.Net",
        "System.Runtime.Serialization", "System.ComponentModel.DataAnnotations",
        // Mezcla: DataContractJsonSerializer está en la BCL moderna; el cliente WCF, en OpenSilver.
        "System.ServiceModel", "System.ServiceModel.Web",
        // UI de Silverlight: cada tipo se reubica en el ensamblado OpenSilver real
        // que lo exporta (p. ej. HtmlPage vive en OpenSilver.Browser, no en OpenSilver).
        "System.Windows", "System.Windows.Browser", "System.Windows.Controls",
        "System.Windows.Controls.Data", "System.Windows.Controls.Data.Input",
        "System.Windows.Controls.Navigation", "System.Windows.Controls.Toolkit",
        "System.Windows.Controls.Layout.Toolkit", "System.Windows.Controls.Input.Toolkit",
        "System.Windows.Controls.DataVisualization.Toolkit", "System.Windows.Data",
    };

    // Tipos de Silverlight que en OpenSilver o en el navegador se llaman o funcionan distinto:
    // se reemplazan por su equivalente.
    private static readonly Dictionary<string, Type> Reemplazos = new(StringComparer.Ordinal)
    {
        ["System.Security.Cryptography.AesManaged"] = typeof(Plata.Compat.AesManaged),
        // Mismo tipo con otro nombre: Application.Host es SilverlightHost en Silverlight y
        // System.Windows.Host en OpenSilver (Content, Settings, Source, InitParams).
        ["System.Windows.Interop.SilverlightHost"] = typeof(System.Windows.Host),
        // WebClient de Silverlight: el de Plata baja por el Portero (ver Plata.Compat.WebClient).
        ["System.Net.WebClient"] = typeof(Plata.Compat.WebClient),
        ["System.Net.OpenReadCompletedEventArgs"] = typeof(Plata.Compat.OpenReadCompletedEventArgs),
        ["System.Net.OpenReadCompletedEventHandler"] = typeof(Plata.Compat.OpenReadCompletedEventHandler),
        ["System.Net.DownloadProgressChangedEventArgs"] = typeof(Plata.Compat.DownloadProgressChangedEventArgs),
        ["System.Net.DownloadProgressChangedEventHandler"] = typeof(Plata.Compat.DownloadProgressChangedEventHandler),
    };

    // Caché global: nombre completo de tipo → ensamblado moderno que lo exporta.
    private static readonly Dictionary<string, string?> _modernaDeTipo = new(StringComparer.Ordinal);

    public byte[] Traducir(byte[] ensamblado, out IReadOnlyList<string> referenciasReescritas)
    {
        var reescritas = new List<string>();
        using var mod = ModuleDefinition.ReadModule(new MemoryStream(ensamblado),
            new ReaderParameters { AssemblyResolver = new SinResolver() });

        var cambiado = UnificarBcl(mod);
        cambiado |= RedirigirUiAOpenSilver(mod, reescritas);
        cambiado |= SaltearApiAusente(mod);
        cambiado |= DesviarLlamadas(mod);
        cambiado |= ReubicarTiposEnAtributos(mod);
        cambiado |= InsertarRastro(mod);
        cambiado |= QuitarAtributosInexistentes(mod);

        referenciasReescritas = reescritas;
        if (!cambiado) return ensamblado;

        using var salida = new MemoryStream();
        mod.Write(salida);
        return salida.ToArray();
    }

    // Reubica cada referencia de tipo de la BCL de Silverlight al ensamblado moderno.
    private static bool UnificarBcl(ModuleDefinition mod)
    {
        var refsModernas = new Dictionary<string, AssemblyNameReference>(StringComparer.Ordinal);
        var cambiado = false;

        foreach (var t in mod.GetTypeReferences())
        {
            if (t.Scope is not AssemblyNameReference an || !BclAUnificar.Contains(an.Name)) continue;
            if (Reemplazos.TryGetValue(t.FullName, out var reemplazo))
            {
                t.Namespace = reemplazo.Namespace;
                t.Name = reemplazo.Name;
                t.Scope = Referencia(mod, refsModernas, reemplazo.Assembly.GetName());
                cambiado = true;
                continue;
            }
            var moderna = ModernaDeTipo(t.FullName.Replace('/', '+'));
            if (moderna is null || moderna == an.Name) continue;

            t.Scope = Referencia(mod, refsModernas, new AssemblyName(moderna) { Version = new Version(0, 0, 0, 0) });
            cambiado = true;
        }
        return cambiado;
    }

    /// <summary>
    /// Para desarrollo: los métodos de la app ("Tipo::Método") en los que se inserta, al comenzar,
    /// una llamada a Plata.Compat.Rastro.Entrar. Null: sin rastreo.
    /// </summary>
    public static Func<string, bool>? Rastrear { get; set; }

    private static bool InsertarRastro(ModuleDefinition mod)
    {
        if (Rastrear is not { } rastrear) return false;
        var cambiado = false;
        MethodReference? entrar = null;
        foreach (var tipo in mod.GetTypes())
            foreach (var m in tipo.Methods)
            {
                var nombre = tipo.FullName + "::" + m.Name;
                if (!m.HasBody || !rastrear(nombre)) continue;
                entrar ??= mod.ImportReference(typeof(Plata.Compat.Rastro).GetMethod(nameof(Plata.Compat.Rastro.Entrar)));
                var il = m.Body.GetILProcessor();
                var primera = m.Body.Instructions[0];
                il.InsertBefore(primera, il.Create(OpCodes.Ldstr, nombre));
                il.InsertBefore(primera, il.Create(OpCodes.Call, entrar));
                cambiado = true;
            }
        return cambiado;
    }

    // Los argumentos de tipo System.Type de los atributos (p. ej. [TemplatePart(Type = typeof(X))])
    // no están en la tabla de referencias: se guardan como texto con el ensamblado de Silverlight.
    // Se reubican igual que las referencias de tipo.
    private static bool ReubicarTiposEnAtributos(ModuleDefinition mod)
    {
        var cambiado = false;
        var refs = new Dictionary<string, AssemblyNameReference>(StringComparer.Ordinal);

        CustomAttributeArgument Reubicar(CustomAttributeArgument arg)
        {
            switch (arg.Value)
            {
                case TypeReference t when arg.Type.FullName == "System.Type":
                    var nuevo = TipoReubicado(mod, refs, t);
                    if (nuevo is null) return arg;
                    cambiado = true;
                    return new CustomAttributeArgument(arg.Type, nuevo);
                case CustomAttributeArgument interno: // valor en caja (object)
                    return new CustomAttributeArgument(arg.Type, Reubicar(interno));
                case CustomAttributeArgument[] arreglo:
                    return new CustomAttributeArgument(arg.Type, arreglo.Select(Reubicar).ToArray());
                default:
                    return arg;
            }
        }

        foreach (var proveedor in ConAtributos(mod))
            foreach (var ca in proveedor.CustomAttributes)
            try
            {
                // Si un argumento es un enum de otro ensamblado, Cecil no puede leer el atributo
                // (no resuelve ensamblados acá): queda sin leer y se escribe tal cual estaba.
                for (var i = 0; i < ca.ConstructorArguments.Count; i++)
                    ca.ConstructorArguments[i] = Reubicar(ca.ConstructorArguments[i]);
                for (var i = 0; i < ca.Properties.Count; i++)
                    ca.Properties[i] = new CustomAttributeNamedArgument(ca.Properties[i].Name, Reubicar(ca.Properties[i].Argument));
                for (var i = 0; i < ca.Fields.Count; i++)
                    ca.Fields[i] = new CustomAttributeNamedArgument(ca.Fields[i].Name, Reubicar(ca.Fields[i].Argument));
            }
            catch (Exception e) when (e is ResolutionException or AssemblyResolutionException) { }
        return cambiado;
    }

    // Nuevo TypeReference para un tipo de Silverlight (null si no hay que moverlo).
    private static TypeReference? TipoReubicado(ModuleDefinition mod, Dictionary<string, AssemblyNameReference> refs, TypeReference t)
    {
        if (t.DeclaringType is { } contenedor)
        {
            var nuevoContenedor = TipoReubicado(mod, refs, contenedor);
            return nuevoContenedor is null ? null : new TypeReference("", t.Name, mod, nuevoContenedor.Scope) { DeclaringType = nuevoContenedor };
        }
        if (t.Scope is not AssemblyNameReference an || !BclAUnificar.Contains(an.Name)) return null;
        if (Reemplazos.TryGetValue(t.FullName, out var reemplazo))
            return new TypeReference(reemplazo.Namespace, reemplazo.Name, mod, Referencia(mod, refs, reemplazo.Assembly.GetName()));
        var moderna = ModernaDeTipo(t.FullName) ?? MapaDeEnsamblados.Destino(an.Name);
        if (moderna is null || moderna == an.Name) return null;
        return new TypeReference(t.Namespace, t.Name, mod,
            Referencia(mod, refs, new AssemblyName(moderna) { Version = new Version(0, 0, 0, 0) }));
    }

    // Cecil no resuelve otros ensamblados: en el navegador no hay de dónde leerlos, y para
    // traducir alcanza con las referencias.
    private sealed class SinResolver : IAssemblyResolver
    {
        public AssemblyDefinition? Resolve(AssemblyNameReference name) => null;
        public AssemblyDefinition? Resolve(AssemblyNameReference name, ReaderParameters parameters) => null;
        public void Dispose() { }
    }

    private static IEnumerable<ICustomAttributeProvider> ConAtributos(ModuleDefinition mod)
    {
        yield return mod.Assembly;
        yield return mod;
        foreach (var t in mod.GetTypes())
        {
            yield return t;
            foreach (var g in t.GenericParameters) yield return g;
            foreach (var f in t.Fields) yield return f;
            foreach (var p in t.Properties) yield return p;
            foreach (var e in t.Events) yield return e;
            foreach (var m in t.Methods)
            {
                yield return m;
                yield return m.MethodReturnType;
                foreach (var p in m.Parameters) yield return p;
                foreach (var g in m.GenericParameters) yield return g;
            }
        }
    }

    // Referencia del módulo al ensamblado dado; la crea una sola vez.
    private static AssemblyNameReference Referencia(
        ModuleDefinition mod, Dictionary<string, AssemblyNameReference> cache, AssemblyName nombre)
    {
        var n = nombre.Name!;
        if (cache.TryGetValue(n, out var r)) return r;
        r = mod.AssemblyReferences.FirstOrDefault(a => a.Name == n)
            ?? new AssemblyNameReference(n, nombre.Version ?? new Version(0, 0, 0, 0));
        if (!mod.AssemblyReferences.Contains(r)) mod.AssemblyReferences.Add(r);
        cache[n] = r;
        return r;
    }

    // Métodos de OpenSilver con firma distinta a Silverlight que la app llama y
    // cuyo efecto es prescindible en un visor: se anula la llamada (pop de args).
    private static readonly HashSet<string> MetodosASaltear = new(StringComparer.Ordinal)
    {
        "System.Windows.Application::add_Exit",
        "System.Windows.Application::remove_Exit",
    };

    // Llamadas cuyo comportamiento cambió respecto de Silverlight: se cambian por el
    // método equivalente de Plata.Compat.Desvios (estático; si la original es de instancia,
    // la instancia pasa primero).
    private static readonly Dictionary<string, System.Reflection.MethodInfo> LlamadasADesviar = new(StringComparer.Ordinal)
    {
        ["System.Windows.FrameworkElement::add_Loaded(System.Windows.RoutedEventHandler)"] =
            typeof(Plata.Compat.Diferido).GetMethod(nameof(Plata.Compat.Diferido.Suscribir))!,
        ["System.Windows.FrameworkElement::remove_Loaded(System.Windows.RoutedEventHandler)"] =
            typeof(Plata.Compat.Diferido).GetMethod(nameof(Plata.Compat.Diferido.Desuscribir))!,
        ["System.Windows.FrameworkElement::get_ActualHeight()"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.AltoReal))!,
        ["System.Windows.FrameworkElement::get_ActualWidth()"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.AnchoReal))!,
        ["System.Windows.Browser.HtmlWindow::Navigate(System.Uri)"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.Navegar))!,
        ["System.Windows.Browser.HtmlWindow::Navigate(System.Uri,System.String)"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.NavegarEn))!,
        ["System.Windows.Browser.HtmlWindow::Navigate(System.Uri,System.String,System.String)"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.NavegarEnCon))!,
        ["System.Windows.Browser.HtmlPage::PopupWindow(System.Uri,System.String,System.Windows.Browser.HtmlPopupWindowOptions)"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.AbrirVentana))!,
        ["System.Windows.Host::get_Source()"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.FuenteDelHost))!,
        ["System.Windows.Application::GetResourceStream(System.Windows.Resources.StreamResourceInfo,System.Uri)"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.LeerDelPaquete))!,
        ["System.Windows.AssemblyPart::Load(System.IO.Stream)"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.CargarParte))!,
        ["System.Runtime.Serialization.Json.DataContractJsonSerializer::ReadObject(System.IO.Stream)"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.LeerJsonDesdeElPrincipio))!,
        ["System.Windows.Application::LoadComponent(System.Object,System.Uri)"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.CargarComponente))!,
        ["System.AppDomain::DefineDynamicAssembly(System.Reflection.AssemblyName,System.Reflection.Emit.AssemblyBuilderAccess)"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.DefinirEnsambladoDinamico))!,
        ["System.Reflection.Emit.AssemblyBuilder::DefineDynamicModule(System.String,System.Boolean)"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.DefinirModuloDinamico))!,
        ["System.Windows.Browser.ScriptObject::Invoke(System.String,System.Object[])"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.InvocarEnPagina))!,
        ["System.ServiceModel.ChannelFactory`1::.ctor(System.ServiceModel.Channels.Binding,System.ServiceModel.EndpointAddress)"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.NuevaFabrica))!,
        ["System.ServiceModel.ChannelFactory`1::CreateChannel()"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.CrearCanal))!,
        ["System.ServiceModel.ICommunicationObject::Close()"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.Cerrar))!,
        ["System.ServiceModel.Channels.CommunicationObject::Close()"] =
            typeof(Plata.Compat.Desvios).GetMethod(nameof(Plata.Compat.Desvios.CerrarObjeto))!,
    };

    private static bool DesviarLlamadas(ModuleDefinition mod)
    {
        var cambiado = false;
        foreach (var tipo in mod.GetTypes())
            foreach (var m in tipo.Methods)
            {
                if (!m.HasBody) continue;
                foreach (var instr in m.Body.Instructions)
                {
                    if (instr.Operand is not MethodReference mr || mr is GenericInstanceMethod) continue;
                    // En un tipo genérico (p. ej. ChannelFactory<TService>) la clave usa el tipo abierto.
                    var generico = mr.DeclaringType as GenericInstanceType;
                    var clave = (generico?.ElementType ?? mr.DeclaringType).FullName + "::" + mr.Name +
                                "(" + string.Join(",", mr.Parameters.Select(p => p.ParameterType.FullName)) + ")";
                    if (!LlamadasADesviar.TryGetValue(clave, out var destino)) continue;
                    // Un constructor se desvía solo en "new"; la llamada al constructor base de una
                    // clase derivada tiene que seguir siendo la original.
                    if (mr.Name == ".ctor" && instr.OpCode != OpCodes.Newobj) continue;

                    instr.OpCode = OpCodes.Call;
                    instr.Operand = MetodoDeReemplazo(mod, mr, generico, destino);
                    cambiado = true;
                }
            }
        return cambiado;
    }

    // Referencia al método estático de reemplazo, con los tipos que el módulo ya usa:
    // (instancia, args...). Si el tipo original es genérico, sus argumentos (!i) pasan a ser
    // argumentos del método (!!i): ChannelFactory<TService>.CreateChannel() →
    // Desvios.CrearCanal<TService>(ChannelFactory<TService>).
    private static MethodReference MetodoDeReemplazo(
        ModuleDefinition mod, MethodReference mr, GenericInstanceType? generico, System.Reflection.MethodInfo destino)
    {
        var compat = new TypeReference(destino.DeclaringType!.Namespace, destino.DeclaringType.Name, mod,
            Referencia(mod, new(), destino.DeclaringType.Assembly.GetName()));
        // "new T(args)" pasa a ser un método estático que devuelve T y recibe solo los args.
        var esNew = mr.Name == ".ctor";
        var nuevo = new MethodReference(destino.Name, esNew ? mr.DeclaringType : mr.ReturnType, compat) { HasThis = false };
        if (generico is null)
        {
            if (mr.HasThis && !esNew) nuevo.Parameters.Add(new ParameterDefinition(mr.DeclaringType));
            foreach (var p in mr.Parameters) nuevo.Parameters.Add(new ParameterDefinition(p.ParameterType));
            return nuevo;
        }

        var propios = generico.GenericArguments.Select((_, i) => new GenericParameter("T" + i, nuevo)).ToList();
        foreach (var gp in propios) nuevo.GenericParameters.Add(gp);
        TypeReference Mapear(TypeReference t) =>
            t is GenericParameter { Type: GenericParameterType.Type } g ? propios[g.Position] : t;

        var abierto = new GenericInstanceType(generico.ElementType);
        foreach (var gp in propios) abierto.GenericArguments.Add(gp);
        nuevo.ReturnType = esNew ? abierto : Mapear(mr.ReturnType);
        if (mr.HasThis && !esNew) nuevo.Parameters.Add(new ParameterDefinition(abierto));
        foreach (var p in mr.Parameters) nuevo.Parameters.Add(new ParameterDefinition(Mapear(p.ParameterType)));

        var instancia = new GenericInstanceMethod(nuevo);
        foreach (var a in generico.GenericArguments) instancia.GenericArguments.Add(a);
        return instancia;
    }

    private static bool SaltearApiAusente(ModuleDefinition mod)
    {
        var cambiado = false;
        foreach (var tipo in mod.GetTypes())
            foreach (var m in tipo.Methods)
            {
                if (!m.HasBody) continue;
                var il = m.Body.GetILProcessor();
                foreach (var instr in m.Body.Instructions.ToList())
                {
                    if (instr.Operand is not MethodReference mr) continue;
                    if (!MetodosASaltear.Contains(mr.DeclaringType.FullName + "::" + mr.Name)) continue;
                    // add_Exit(this, delegate) -> void: descartar los 2 valores de la pila.
                    var pop1 = Instruction.Create(OpCodes.Pop);
                    il.Replace(instr, pop1);
                    il.InsertAfter(pop1, Instruction.Create(OpCodes.Pop));
                    cambiado = true;
                }
            }
        return cambiado;
    }

    // Atributos de ensamblado/módulo propios de Silverlight (p. ej. AssemblyTrademark)
    // que no existen en .NET moderno: el runtime falla al leerlos, y no aportan nada.
    private static bool QuitarAtributosInexistentes(ModuleDefinition mod)
    {
        var cambiado = false;
        foreach (var provider in new ICustomAttributeProvider[] { mod.Assembly, mod })
            for (var i = provider.CustomAttributes.Count - 1; i >= 0; i--)
            {
                var tipo = provider.CustomAttributes[i].AttributeType;
                if (tipo.Scope is not AssemblyNameReference an || !BclAUnificar.Contains(an.Name)) continue;
                if (ModernaDeTipo(tipo.FullName.Replace('/', '+')) is not null) continue;
                provider.CustomAttributes.RemoveAt(i);
                cambiado = true;
            }
        return cambiado;
    }

    // Satélites de OpenSilver donde viven los tipos de UI. Se cargan antes de buscar,
    // para que el resultado no dependa de qué se cargó antes (y quede bien en la caché).
    private static readonly string[] SatelitesDeOpenSilver =
    {
        "OpenSilver.Browser", "OpenSilver.Controls", "OpenSilver.Controls.Data",
        "OpenSilver.Controls.Input", "OpenSilver.Controls.Navigation",
        "OpenSilver.Controls.Toolkit", "OpenSilver.Controls.Data.Input",
        "OpenSilver.Controls.Layout.Toolkit", "OpenSilver.Controls.Input.Toolkit",
        "OpenSilver.Controls.DataVisualization.Toolkit",
    };

    private static readonly Lazy<bool> _satelitesCargados = new(() =>
    {
        foreach (var n in SatelitesDeOpenSilver) IntentarCargar(n);
        return true;
    });

    private static string? ModernaDeTipo(string fullName)
    {
        if (_modernaDeTipo.TryGetValue(fullName, out var cache)) return cache;
        _ = _satelitesCargados.Value;
        var tipo = Type.GetType(fullName) ?? BuscarEnCargados(fullName) ?? BuscarPorEspacioDeNombres(fullName);
        var nombre = tipo?.Assembly.GetName().Name;
        _modernaDeTipo[fullName] = nombre;
        return nombre;
    }

    private static Type? BuscarEnCargados(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? tipo;
            try { tipo = asm.GetType(fullName); } catch { tipo = null; }
            if (tipo is not null) return tipo;
        }
        return null;
    }

    // En .NET moderno las fachadas se llaman como su espacio de nombres (p. ej.
    // System.Runtime.Serialization.Json): se prueba cargar el espacio y sus padres.
    private static Type? BuscarPorEspacioDeNombres(string fullName)
    {
        var ns = fullName[..Math.Max(0, fullName.LastIndexOf('.'))];
        for (; ns.Length > 0; ns = ns[..Math.Max(0, ns.LastIndexOf('.'))])
        {
            Type? tipo;
            try { tipo = IntentarCargar(ns)?.GetType(fullName); } catch { tipo = null; }
            if (tipo is not null) return tipo;
        }
        return null;
    }

    private static System.Reflection.Assembly? IntentarCargar(string nombre)
    {
        try { return System.Reflection.Assembly.Load(new AssemblyName(nombre)); }
        catch { return null; }
    }

    // System.Windows* (UI de Silverlight) → OpenSilver.
    private static bool RedirigirUiAOpenSilver(ModuleDefinition mod, List<string> reescritas)
    {
        var aRedirigir = mod.AssemblyReferences
            .Where(a => MapaDeEnsamblados.Destino(a.Name) == "OpenSilver")
            .ToList();
        if (aRedirigir.Count == 0) return false;

        var osRef = mod.AssemblyReferences.FirstOrDefault(a => a.Name == "OpenSilver");
        if (osRef is null)
        {
            osRef = aRedirigir[0];
            reescritas.Add(osRef.Name);
            osRef.Name = "OpenSilver";
            osRef.PublicKeyToken = Array.Empty<byte>();
            osRef.Version = OpenSilverVersion;
            aRedirigir.RemoveAt(0);
        }

        foreach (var r in aRedirigir)
        {
            reescritas.Add(r.Name);
            foreach (var t in mod.GetTypeReferences())
                if (ReferenceEquals(t.Scope, r)) t.Scope = osRef;
            mod.AssemblyReferences.Remove(r);
        }
        return true;
    }
}
