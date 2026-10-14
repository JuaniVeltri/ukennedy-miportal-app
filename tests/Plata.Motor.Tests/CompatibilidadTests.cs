// SPDX-License-Identifier: MIT
using Plata.Motor.Informe;
using Plata.Motor.Pantallas;
using Xunit;

public class CompatibilidadTests
{
    private const string XamlConEvento = """
        <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Button x:Name="boton" Content="Hola" Click="Boton_Click" />
        </UserControl>
        """;

    private sealed class ConManejador
    {
        public void Boton_Click(object s, object e) { }
    }

    private sealed class SinManejador { }

    [Fact]
    public void Verificar_metodoPresente_noAnota()
    {
        var informe = new InformeDeCompatibilidad();
        VerificadorDeEventos.Verificar(typeof(ConManejador), XamlConEvento, informe);
        Assert.Empty(informe.Entradas);
    }

    [Fact]
    public void Verificar_metodoAusente_anotaConNombre()
    {
        var informe = new InformeDeCompatibilidad();
        VerificadorDeEventos.Verificar(typeof(SinManejador), XamlConEvento, informe);
        Assert.Contains(informe.Entradas, e => e.Detalle.Contains("Boton_Click"));
    }

    [Fact]
    public void Verificar_binding_noSeConfundeConEvento()
    {
        // Un valor con llaves es un binding, no un método; no debe anotarse.
        var xaml = """
            <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TextBlock Text="{Binding Nombre}" />
            </UserControl>
            """;
        var informe = new InformeDeCompatibilidad();
        VerificadorDeEventos.Verificar(typeof(SinManejador), xaml, informe);
        Assert.Empty(informe.Entradas);
    }

    [Fact]
    public void Informe_aJson_incluyeAppYFaltantes()
    {
        var informe = new InformeDeCompatibilidad();
        informe.Anotar("evento", "Boton_Click ausente");
        var json = informe.AJson("MiApp", "1.0");
        Assert.Contains("MiApp", json);
        Assert.Contains("Boton_Click", json);
    }

    [Fact]
    public void Informe_noDuplicaEntradas()
    {
        var informe = new InformeDeCompatibilidad();
        informe.Anotar("api", "Algo");
        informe.Anotar("api", "Algo");
        Assert.Single(informe.Entradas);
    }
}
