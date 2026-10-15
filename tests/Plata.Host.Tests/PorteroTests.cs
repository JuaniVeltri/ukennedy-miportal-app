// SPDX-License-Identifier: MIT
using System.Net;
using System.Text;
using System.Text.Json;
using Plata.Host;
using Xunit;

public class PorteroTests
{
    [Theory]
    [InlineData("http://servicios1.kennedy.edu.ar/AcademicoServices/X.svc", true)]
    [InlineData("https://kennedy.edu.ar/", true)]
    [InlineData("http://kennedy.edu.ar.malo.com/", false)]
    [InlineData("http://otrokennedy.edu.ar/", false)]
    [InlineData("http://127.0.0.1/", false)]
    [InlineData("file:///etc/passwd", false)]
    public void Permitido_soloLosDominiosConfigurados(string url, bool esperado)
    {
        var portero = new Portero(new[] { "kennedy.edu.ar" });
        Assert.Equal(esperado, portero.Permitido(new Uri(url)));
    }

    [Fact]
    public async Task Atender_dominioNoPermitido_noSaleALaRed()
    {
        var portero = new Portero(Array.Empty<string>());
        var r = Leer(await portero.AtenderAsync(Pedido(1, "http://servicios1.kennedy.edu.ar/X.svc", "x")));

        Assert.Equal(1, r.GetProperty("id").GetInt32());
        Assert.Equal(0, r.GetProperty("estado").GetInt32());
        Assert.Contains("no permitido", r.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Atender_reenviaMetodoCabecerasYCuerpo()
    {
        var puerto = new Random().Next(40001, 60000);
        var prefijo = $"http://localhost:{puerto}/";
        using var l = new HttpListener();
        l.Prefixes.Add(prefijo);
        l.Start();
        string? accion = null, tipo = null, cuerpo = null;
        var servido = Task.Run(async () =>
        {
            var c = await l.GetContextAsync();
            accion = c.Request.Headers["SOAPAction"];
            tipo = c.Request.ContentType;
            cuerpo = await new StreamReader(c.Request.InputStream).ReadToEndAsync();
            c.Response.ContentType = "text/xml; charset=utf-8";
            var b = Encoding.UTF8.GetBytes("<respuesta/>");
            await c.Response.OutputStream.WriteAsync(b);
            c.Response.Close();
        });

        var portero = new Portero(new[] { "localhost" });
        var r = Leer(await portero.AtenderAsync(Pedido(7, prefijo + "X.svc", "<sobre/>",
            ("Content-Type", "text/xml; charset=utf-8"), ("SOAPAction", "\"urn:accion\""))));
        await servido;

        Assert.Equal(7, r.GetProperty("id").GetInt32());
        Assert.Equal(200, r.GetProperty("estado").GetInt32());
        Assert.Equal("<respuesta/>", Encoding.UTF8.GetString(Convert.FromBase64String(r.GetProperty("cuerpo").GetString()!)));
        Assert.Contains(r.GetProperty("cabeceras").EnumerateArray(),
            c => c[0].GetString() == "Content-Type" && c[1].GetString()!.StartsWith("text/xml"));
        Assert.Equal("\"urn:accion\"", accion);
        Assert.StartsWith("text/xml", tipo);
        Assert.Equal("<sobre/>", cuerpo);
    }

    [Fact]
    public async Task Traer_archivoDelSitioDeOrigen()
    {
        var puerto = new Random().Next(40001, 60000);
        var prefijo = $"http://localhost:{puerto}/";
        using var l = new HttpListener();
        l.Prefixes.Add(prefijo);
        l.Start();
        string? pedida = null;
        var servido = Task.Run(async () =>
        {
            var c = await l.GetContextAsync();
            pedida = c.Request.Url!.AbsolutePath;
            c.Response.ContentType = "image/png";
            await c.Response.OutputStream.WriteAsync(new byte[] { 1, 2, 3 });
            c.Response.Close();
        });

        var portero = new Portero(new[] { "localhost" });
        var (datos, tipo) = await portero.TraerAsync(new Uri(prefijo + "ClientBin/images/common/x.png"));
        await servido;

        Assert.Equal(new byte[] { 1, 2, 3 }, datos);
        Assert.Equal("image/png", tipo);
        Assert.Equal("/ClientBin/images/common/x.png", pedida);
    }

    [Fact]
    public async Task Traer_dominioNoPermitido_noSaleALaRed()
    {
        var portero = new Portero(Array.Empty<string>());
        var (datos, _) = await portero.TraerAsync(new Uri("http://servicios.kennedy.edu.ar/x.png"));
        Assert.Null(datos);
    }

    private static string Pedido(int id, string url, string cuerpo, params (string, string)[] cabeceras)
        => JsonSerializer.Serialize(new
        {
            id,
            metodo = "POST",
            url,
            cabeceras = cabeceras.Select(c => new[] { c.Item1, c.Item2 }),
            cuerpo = Convert.ToBase64String(Encoding.UTF8.GetBytes(cuerpo)),
        });

    private static JsonElement Leer(string json) => JsonDocument.Parse(json).RootElement;
}

public class PorteroCookiesTests
{
    [Fact]
    public void AgregarCookies_soloDeDominiosPermitidos()
    {
        var portero = new Portero(new[] { "kennedy.edu.ar" });
        var n = portero.AgregarCookies(new[]
        {
            new System.Net.Cookie("s", "1", "/", "servicios.kennedy.edu.ar"),
            new System.Net.Cookie("t", "2", "/", ".kennedy.edu.ar"),
            new System.Net.Cookie("u", "3", "/", "google.com"),
        });

        Assert.Equal(2, n);
    }
}
