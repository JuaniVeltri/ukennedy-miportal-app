// SPDX-License-Identifier: MIT
using Plata.Motor.Puente;
using Plata.Motor.Xap;
using Xunit;

public class PlanDeCargaTests
{
    // Manifiesto representativo de las apps reales (solo nombres, no binarios).
    private static AppManifest Manifiesto() => new(
        "GGI.Academico.UI.Silverlight.App", "GGI.Academico.UI.Silverlight.App.App", "3.0.40624.0",
        new[]
        {
            new AssemblyPart("GGI.Academico.UI.Silverlight.App.dll"),
            new AssemblyPart("Bpf.Silverlight.dll"),
            new AssemblyPart("SilverlightContrib.dll"),
            new AssemblyPart("System.Windows.Controls.dll"),        // SDK: lo provee OpenSilver
            new AssemblyPart("System.Windows.Controls.Toolkit.dll"), // SDK
            new AssemblyPart("System.Windows.Data.dll"),             // SDK
            new AssemblyPart("es/System.Windows.Controls.resources.dll"), // recurso de idioma
        });

    [Fact]
    public void AcCargar_incluyeAppYterceros()
    {
        var cargar = PlanDeCarga.AcCargar(Manifiesto()).Select(p => p.Source).ToHashSet();
        Assert.Contains("GGI.Academico.UI.Silverlight.App.dll", cargar);
        Assert.Contains("Bpf.Silverlight.dll", cargar);
        Assert.Contains("SilverlightContrib.dll", cargar);
    }

    [Fact]
    public void AcCargar_salteaSdkYrecursos()
    {
        var cargar = PlanDeCarga.AcCargar(Manifiesto()).Select(p => p.Source).ToHashSet();
        Assert.DoesNotContain("System.Windows.Controls.dll", cargar);
        Assert.DoesNotContain("System.Windows.Controls.Toolkit.dll", cargar);
        Assert.DoesNotContain("System.Windows.Data.dll", cargar);
        Assert.DoesNotContain("es/System.Windows.Controls.resources.dll", cargar);
    }
}
