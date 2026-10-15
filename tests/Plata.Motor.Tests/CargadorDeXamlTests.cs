// SPDX-License-Identifier: MIT
using System.Resources;
using Plata.Compat;
using Xunit;

public class CargadorDeXamlTests
{
    [Fact]
    public void Ubicacion_deComponente_separaEnsambladoYRuta()
    {
        var (ensamblado, ruta) = CargadorDeXaml.Ubicacion(
            new Uri("/GGI.Core.UI.Silverlight;component/Controls/Shell/Layouts/SingleShellLayout.xaml", UriKind.Relative));

        Assert.Equal("GGI.Core.UI.Silverlight", ensamblado);
        Assert.Equal("controls/shell/layouts/singleshelllayout.xaml", ruta);
    }

    [Fact]
    public void Ubicacion_conEspaciosEscapados_seDesescapa()
    {
        var (_, ruta) = CargadorDeXaml.Ubicacion(new Uri("/App;component/Mis%20Vistas/A.xaml", UriKind.Relative));
        Assert.Equal("mis vistas/a.xaml", ruta);
    }

    [Fact]
    public void Adaptar_loadedDelXaml_pasaAlCargadoDiferidoDePlata()
    {
        const string xaml = """
            <?xml version="1.0" encoding="utf-8"?>
            <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                Loaded="Control_Loaded" IsLoaded="x">
                <Grid Loaded="Grid_Loaded"/>
            </UserControl>
            """;

        var adaptado = CargadorDeXaml.Adaptar(xaml);

        Assert.Contains("<UserControl xmlns:plata=\"clr-namespace:Plata.Compat;assembly=Plata.Compat\"", adaptado);
        Assert.Contains("plata:Diferido.Cargado=\"Control_Loaded\"", adaptado);
        Assert.Contains("<Grid plata:Diferido.Cargado=\"Grid_Loaded\"/>", adaptado);
        Assert.Contains("IsLoaded=\"x\"", adaptado);
        Assert.DoesNotContain(" Loaded=", adaptado);
    }

    [Fact]
    public void Adaptar_gruposDeEstados_pasanAlGetterDePlata()
    {
        const string xaml = """
            <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:vsm="clr-namespace:System.Windows;assembly=System.Windows">
                <ControlTemplate><Grid>
                    <vsm:VisualStateManager.VisualStateGroups><vsm:VisualStateGroup x:Name="A"/></vsm:VisualStateManager.VisualStateGroups>
                </Grid></ControlTemplate>
                <Grid><VisualStateManager.VisualStateGroups></VisualStateManager.VisualStateGroups></Grid>
            </UserControl>
            """;

        var adaptado = CargadorDeXaml.Adaptar(xaml);

        Assert.Contains("xmlns:plata=\"clr-namespace:Plata.Compat;assembly=Plata.Compat\"", adaptado);
        Assert.Contains("<plata:Estados.Grupos><vsm:VisualStateGroup x:Name=\"A\"/></plata:Estados.Grupos>", adaptado);
        // El contenido del template también conoce el prefijo plata.
        Assert.Matches("<Grid [^>]*xmlns:plata=", adaptado);
        Assert.Contains("<Grid><plata:Estados.Grupos></plata:Estados.Grupos></Grid>", adaptado);
        Assert.DoesNotContain("VisualStateManager.VisualStateGroups", adaptado);
    }

    [Fact]
    public void Adaptar_contenidoDeTemplates_llevaLasDeclaracionesXmlnsDeLaRaiz()
    {
        const string xaml = """
            <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:ctrl="clr-namespace:App;assembly=App">
                <ControlTemplate TargetType="Button">
                    <Grid x:Name="raiz"><Style TargetType="ToggleButton"/></Grid>
                </ControlTemplate>
                <DataTemplate><ctrl:Item xmlns:ctrl="clr-namespace:Otra;assembly=Otra"/></DataTemplate>
                <ControlTemplate TargetType="Button"/>
            </UserControl>
            """;

        var adaptado = CargadorDeXaml.Adaptar(xaml);

        Assert.Contains("<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:ctrl=\"clr-namespace:App;assembly=App\" x:Name=\"raiz\">", adaptado);
        // Lo que el elemento ya declara no se repite.
        Assert.Contains("<ctrl:Item xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:ctrl=\"clr-namespace:Otra;assembly=Otra\"/>", adaptado);
    }

    [Fact]
    public void Adaptar_xNameEnRecursos_tambienEsClave()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid.Resources>
                    <Style TargetType="ToggleButton" x:Name="comboToggleStyle"><Setter Property="Tag" Value="a"/></Style>
                    <SolidColorBrush x:Key="ya" x:Name="conClave"/>
                </Grid.Resources>
                <Border x:Name="noEsRecurso"/>
            </Grid>
            """;

        var adaptado = CargadorDeXaml.Adaptar(xaml);

        Assert.Contains("<Style x:Key=\"comboToggleStyle\" TargetType=\"ToggleButton\" x:Name=\"comboToggleStyle\">", adaptado);
        Assert.Contains("<SolidColorBrush x:Key=\"ya\" x:Name=\"conClave\"/>", adaptado);
        Assert.Contains("<Border x:Name=\"noEsRecurso\"/>", adaptado);
        Assert.Contains("<Setter Property=\"Tag\" Value=\"a\"/>", adaptado);
    }

    // Raíz de prueba con handlers como los de un code-behind.
    private sealed class PantallaDePrueba
    {
        private void Grilla_LoadingRow(object sender, EventArgs e) { }
        private void Boton_Click(object sender, EventArgs e) { }
        public string Hola() => "no es handler";
    }

    [Fact]
    public void EventosEnTemplates_seConectanALaRaizDelDocumento()
    {
        const string xaml = """
            <UserControl xmlns="p" Loaded="NoEstaEnTemplate">
                <DataTemplate><Grid>
                    <ctrl:Grilla Grid.Row="1" LoadingRow="Grilla_LoadingRow" Text="Hola" Tag="Boton_Click"/>
                </Grid></DataTemplate>
                <Button Click="Boton_Click"/>
            </UserControl>
            """;

        var adaptado = CargadorDeXaml.EventosEnTemplates(xaml, typeof(PantallaDePrueba), 7);

        Assert.Contains("<ctrl:Grilla Grid.Row=\"1\" Text=\"Hola\" plata:Eventos.Conectar=\"7|LoadingRow:Grilla_LoadingRow;Tag:Boton_Click\"/>", adaptado);
        // Fuera de los templates no se toca (ahí OpenSilver los conecta bien).
        Assert.Contains("<Button Click=\"Boton_Click\"/>", adaptado);
        Assert.Contains("Loaded=\"NoEstaEnTemplate\"", adaptado);
    }

    [Fact]
    public void Adaptar_bindingsOneTime_seMarcanParaRefrescarAlCargar()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <TextBlock Text="{Binding Mode=OneTime, Converter={StaticResource T}, ConverterParameter=A}" Tag="{Binding X, Mode=OneTime}" Foreground="White"/>
                <TextBlock Text="{Binding Nombre}"/>
            </Grid>
            """;

        var adaptado = CargadorDeXaml.Adaptar(xaml);

        Assert.Contains("Foreground=\"White\" plata:Enlaces.Refrescar=\"Text,Tag\"/>", adaptado);
        Assert.Contains("<TextBlock Text=\"{Binding Nombre}\"/>", adaptado);
        Assert.Contains("xmlns:plata=", adaptado);
    }

    [Fact]
    public void Adaptar_presentadoresEnControlTemplate_tomanElContenidoDelControl()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <ControlTemplate TargetType="ScrollViewer"><Grid>
                    <ScrollContentPresenter x:Name="ScrollContentPresenter" ContentTemplate="{TemplateBinding ContentTemplate}"/>
                    <ContentPresenter Content="{TemplateBinding Header}"/>
                    <ContentPresenter></ContentPresenter>
                </Grid></ControlTemplate>
                <DataTemplate><ContentPresenter/></DataTemplate>
                <ControlTemplate TargetType="ComboBox"><Grid><ContentPresenter x:Name="NoContentControl"/></Grid></ControlTemplate>
                <ControlTemplate TargetType="Button"><Grid><ContentPresenter x:Name="ConHijo"><TextBlock Text=" "/></ContentPresenter></Grid></ControlTemplate>
            </Grid>
            """;

        var adaptado = CargadorDeXaml.Adaptar(xaml);

        Assert.Contains("<ScrollContentPresenter x:Name=\"ScrollContentPresenter\" ContentTemplate=\"{TemplateBinding ContentTemplate}\" Content=\"{TemplateBinding Content}\"/>", adaptado);
        Assert.Contains("<ContentPresenter Content=\"{TemplateBinding Header}\"/>", adaptado);
        Assert.Contains("<ContentPresenter Content=\"{TemplateBinding Content}\" ContentTemplate=\"{TemplateBinding ContentTemplate}\"></ContentPresenter>", adaptado);
        // En un DataTemplate no hay control del cual tomarlo.
        Assert.Matches("<DataTemplate><ContentPresenter( xmlns[^>]*)?/></DataTemplate>", adaptado);
        // Solo si el control del template es un ContentControl (ComboBox no lo es).
        Assert.Contains("<ContentPresenter x:Name=\"NoContentControl\"/>", adaptado);
        // Si el presentador ya tiene contenido como hijo, no se toca.
        Assert.Contains("<ContentPresenter x:Name=\"ConHijo\"><TextBlock", adaptado);
    }

    [Theory]
    // Relativa: a la carpeta del XAML que la contiene.
    [InlineData("DefaultGGIStyles/CommonStyles.xaml", "Estilos", "ggistyles/defaultggistyles.xaml", "Estilos", "ggistyles/defaultggistyles/commonstyles.xaml")]
    [InlineData("../Comun/Botones.xaml", "App", "vistas/a/pantalla.xaml", "App", "vistas/comun/botones.xaml")]
    // Con barra inicial: desde la raíz del ensamblado.
    [InlineData("/Temas/Base.xaml", "App", "vistas/pantalla.xaml", "App", "temas/base.xaml")]
    // De componente: el ensamblado que indica.
    [InlineData("/GGI.Core.UI.Silverlight.Styles;component/GGIStyles/DefaultGGIStyles.xaml", "App", "vistas/pantalla.xaml",
        "GGI.Core.UI.Silverlight.Styles", "ggistyles/defaultggistyles.xaml")]
    public void ResolverFuente_comoSilverlight(string fuente, string ensamblado, string ruta, string ensambladoEsperado, string rutaEsperada)
    {
        var (e, r) = CargadorDeXaml.ResolverFuente(new Uri(fuente, UriKind.Relative), ensamblado, ruta);
        Assert.Equal(ensambladoEsperado, e);
        Assert.Equal(rutaEsperada, r);
    }

    [Fact]
    public void LeerDeRecursos_encuentraLaPantallaSinDistinguirMayusculas()
    {
        // Silverlight guarda el XAML de cada pantalla en <Ensamblado>.g.resources,
        // con la ruta en minúsculas y el contenido como stream.
        var recursos = new MemoryStream();
        using (var w = new ResourceWriter(recursos))
        {
            w.AddResource("views/otra.xaml", new MemoryStream("<Otra/>"u8.ToArray()));
            w.AddResource("controls/pantalla.xaml", new MemoryStream("﻿<Pantalla/>"u8.ToArray()));
            w.Generate();
            recursos.Position = 0;
            Assert.Equal("<Pantalla/>", CargadorDeXaml.LeerDeRecursos(recursos, "controls/pantalla.xaml"));
        }
    }

    [Fact]
    public void Adaptar_ensambladosDeSilverlight_vanAlEspacioDePresentacion()
    {
        const string xaml = """
            <UserControl x:Class="App.Vista"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:data="clr-namespace:System.Windows.Controls;assembly=System.Windows.Controls.Data"
                xmlns:sw="clr-namespace:System.Windows;assembly=System.Windows"
                xmlns:sys="clr-namespace:System;assembly=mscorlib"
                xmlns:ggi="clr-namespace:GGI.Core.UI.Silverlight.Controls;assembly=GGI.Core.UI.Silverlight">
            </UserControl>
            """;

        var adaptado = CargadorDeXaml.Adaptar(xaml);

        const string presentacion = "\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
        Assert.Contains("xmlns:data=" + presentacion, adaptado);
        Assert.Contains("xmlns:sw=" + presentacion, adaptado);
        Assert.Contains("xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"", adaptado);
        Assert.Contains("assembly=GGI.Core.UI.Silverlight\"", adaptado);
        // x:Class es para el compilador; la instancia ya existe.
        Assert.DoesNotContain("x:Class", adaptado);
    }

    [Fact]
    public void Adaptar_transformacionDeUnElementoConEfecto_vaDesdeLaEsquina()
    {
        // El rótulo vertical del menú de GGI: en Silverlight sale sobre la franja (origen 0,0).
        const string xaml = """
            <Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TextBlock x:Name="tblMenuLabel" RenderTransformOrigin="0.5,0.5" Foreground="White">
                <TextBlock.Effect><DropShadowEffect BlurRadius="3"/></TextBlock.Effect>
                <TextBlock.RenderTransform><TransformGroup><RotateTransform Angle="90"/><TranslateTransform X="18" Y="5"/></TransformGroup></TextBlock.RenderTransform>
              </TextBlock>
              <Grid RenderTransformOrigin="0.5,0.5"><Grid.RenderTransform><ScaleTransform/></Grid.RenderTransform></Grid>
              <Border RenderTransformOrigin="0.5,0.5"><Border.Effect><BlurEffect/></Border.Effect></Border>
            </Canvas>
            """;

        var adaptado = CargadorDeXaml.Adaptar(xaml);

        Assert.Contains("<TextBlock x:Name=\"tblMenuLabel\" RenderTransformOrigin=\"0,0\"", adaptado);
        // Sin efecto, o sin transformación, el origen queda como estaba.
        Assert.Contains("<Grid RenderTransformOrigin=\"0.5,0.5\">", adaptado);
        Assert.Contains("<Border RenderTransformOrigin=\"0.5,0.5\">", adaptado);
    }
}
