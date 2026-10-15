// SPDX-License-Identifier: MIT
using System.Net;
using System.Text;
using Plata.Host;
using Xunit;

public class LanzadorTests
{
    [Theory]
    [InlineData("uksv:https://portal.ejemplo.edu.ar/UKSV/Launcher.aspx?t=1&h=2", "https://portal.ejemplo.edu.ar/UKSV/Launcher.aspx?t=1&h=2&v=3")]
    [InlineData("UKSV:https://portal.ejemplo.edu.ar/L.aspx", "https://portal.ejemplo.edu.ar/L.aspx?v=3")]
    [InlineData("uksv:https://malo.com/L.aspx?t=1", null)]
    [InlineData("uksv:file:///etc/passwd", null)]
    [InlineData("https://portal.ejemplo.edu.ar/L.aspx", null)]
    public void Direccion_soloEnlacesUksvDeDominiosPermitidos(string enlace, string? esperada)
    {
        var lanzador = new Lanzador(new Portero(new[] { "ejemplo.edu.ar" }));
        Assert.Equal(esperada, lanzador.Direccion(enlace)?.AbsoluteUri);
    }

    [Fact]
    public void LeerPagina_redireccionPorJavaScript()
    {
        var p = Lanzador.LeerPagina("<script>//<![CDATA[\nwindow.location = 'http://servicios.ejemplo.edu.ar/App/Gestion.aspx';//]]></script>",
            new Uri("https://portal.ejemplo.edu.ar/UKSV/Launcher.aspx?t=1"));

        Assert.Null(p.Xap);
        Assert.Equal("http://servicios.ejemplo.edu.ar/App/Gestion.aspx", p.Siguiente?.AbsoluteUri);
    }

    [Fact]
    public void LeerPagina_objetoDentroDeJavaScript_deAspNetAjax()
    {
        // Sys.UI.Silverlight.Control: el <object> va escapado dentro de un string de JavaScript.
        var html = "<title> Gestión Académica </title><script>Sys.Application.add_init(function() { $create(Sys.UI.Silverlight.Control, " +
                   "{\"source\":\"ClientBin/App.xap\"}, null, null, $get(\"x\")); var h = '\\u003cparam name=\\\"InitParams\\\" value=\\\"A=1,B=x\\u0026amp;y\\\" /\\u003e'; });</script>";

        var p = Lanzador.LeerPagina(html, new Uri("http://servicios.ejemplo.edu.ar/App/Gestion.aspx"));

        Assert.Equal("http://servicios.ejemplo.edu.ar/App/ClientBin/App.xap", p.Xap?.AbsoluteUri);
        Assert.Equal("A=1,B=x&y", p.InitParams);
        Assert.Equal("Gestión Académica", p.Titulo);
    }

    [Fact]
    public async Task Lanzar_sigueElLanzadorHastaLaAppYGuardaLaSesion()
    {
        var puerto = new Random().Next(40001, 60000);
        var b = $"http://localhost:{puerto}/";
        using var l = new HttpListener();
        l.Prefixes.Add(b);
        l.Start();
        var servido = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                var c = await l.GetContextAsync();
                string html;
                if (c.Request.Url!.AbsolutePath == "/UKSV/Launcher.aspx")
                {
                    Assert.Equal("1", c.Request.QueryString["t"]);
                    Assert.Equal("3", c.Request.QueryString["v"]);
                    c.Response.AppendCookie(new Cookie("GGISECURECOOK", "sesion", "/"));
                    html = $"<script>window.location = '{b}App/Gestion.aspx';</script>";
                }
                else
                {
                    Assert.Equal("sesion", c.Request.Cookies["GGISECURECOOK"]?.Value);
                    html = "<title>App</title><object><param name=\"source\" value=\"ClientBin/App.xap\"/><param name=\"initParams\" value=\"K=V\"/></object>";
                }
                var bytes = Encoding.UTF8.GetBytes(html);
                c.Response.ContentType = "text/html; charset=utf-8";
                await c.Response.OutputStream.WriteAsync(bytes);
                c.Response.Close();
            }
        });

        var lanzamiento = await new Lanzador(new Portero(new[] { "localhost" })).LanzarAsync($"uksv:{b}UKSV/Launcher.aspx?t=1");
        await servido;

        Assert.Equal($"{b}App/ClientBin/App.xap", lanzamiento.Xap.AbsoluteUri);
        Assert.Equal($"{b}App/Gestion.aspx", lanzamiento.Pagina?.AbsoluteUri);
        Assert.Equal("K=V", lanzamiento.InitParams);
        Assert.Equal("App", lanzamiento.TituloCorto);
        Assert.Contains(lanzamiento.Cookies, c => c.Name == "GGISECURECOOK" && c.Value == "sesion");

    }

    [Theory]
    [InlineData("Universidad Kennedy - UK Académico - 2", "UK Académico")]
    [InlineData("Cuentas", "Cuentas")]
    [InlineData(null, "GGI.App")]
    public void TituloCorto_laParteQueNombraLaApp(string? titulo, string esperado)
    {
        var l = new Lanzamiento(new Uri("http://x.edu.ar/ClientBin/GGI.App.xap"), null, "", [], Titulo: titulo);
        Assert.Equal(esperado, l.TituloCorto);
    }

    [Fact]
    public async Task Lanzar_enlaceVencido_explicaQuePaso()
    {
        var puerto = new Random().Next(40001, 60000);
        var b = $"http://localhost:{puerto}/";
        using var l = new HttpListener();
        l.Prefixes.Add(b);
        l.Start();
        var servido = Task.Run(async () =>
        {
            var c = await l.GetContextAsync();
            var bytes = Encoding.UTF8.GetBytes("<html><body><div>El enlace ha expirado.</div></body></html>");
            await c.Response.OutputStream.WriteAsync(bytes);
            c.Response.Close();
        });

        var e = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new Lanzador(new Portero(new[] { "localhost" })).LanzarAsync($"uksv:{b}UKSV/Launcher.aspx?t=1"));
        await servido;
        Assert.Contains("El enlace ha expirado.", e.Message);
    }
}
