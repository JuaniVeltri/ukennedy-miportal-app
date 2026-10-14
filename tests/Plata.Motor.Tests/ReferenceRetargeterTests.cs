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
}
