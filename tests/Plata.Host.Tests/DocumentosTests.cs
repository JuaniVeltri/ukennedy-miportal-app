// SPDX-License-Identifier: MIT
using System.Text;
using Plata.Host;
using Xunit;

public class DocumentosTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "plata-test-docs-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _abiertos = new();

    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    private Documentos Con(Func<Uri, Task<Documento?>> traer)
        => new(u => u.Host.EndsWith("ejemplo.edu.ar"), traer, _abiertos.Add, _carpeta);

    [Fact]
    public async Task Pdf_permitido_seBajaYSeAbreElArchivo()
    {
        var url = new Uri("http://servicios.ejemplo.edu.ar/App/Documents/Estado.aspx?L=1");
        var docs = Con(_ => Task.FromResult<Documento?>(new Documento(Encoding.ASCII.GetBytes("%PDF-1.4"), "application/pdf", null, url)));

        var abierto = await docs.AbrirAsync(url.AbsoluteUri);

        Assert.EndsWith(".pdf", abierto);
        Assert.Equal(abierto, Assert.Single(_abiertos));
        Assert.Equal("%PDF-1.4", File.ReadAllText(abierto!));
    }

    [Fact]
    public async Task Html_seGuardaConBaseParaQueCarguenSusRecursos()
    {
        var final = new Uri("http://servicios.ejemplo.edu.ar/App/Documents/Estado.aspx?L=1");
        var docs = Con(_ => Task.FromResult<Documento?>(new Documento(
            Encoding.Latin1.GetBytes("<html><head><title>Estado académico</title></head></html>"), "text/html; charset=iso-8859-1", null, final)));

        var abierto = await docs.AbrirAsync(final.AbsoluteUri);

        Assert.EndsWith(".html", abierto);
        var html = Encoding.Latin1.GetString(File.ReadAllBytes(abierto!));
        Assert.StartsWith("<html><head><base href=\"http://servicios.ejemplo.edu.ar/App/Documents/Estado.aspx?L=1\">", html);
        Assert.Contains("académico", html);
    }

    [Fact]
    public async Task NombreSugerido_daElNombreYLaExtension()
    {
        var url = new Uri("http://servicios.ejemplo.edu.ar/x.aspx");
        var docs = Con(_ => Task.FromResult<Documento?>(new Documento(new byte[] { 1 }, "application/octet-stream", "\"../Plan de pagos.xls\"", url)));

        var abierto = await docs.AbrirAsync(url.AbsoluteUri);

        Assert.StartsWith(Path.Combine(_carpeta, "Plan de pagos-"), abierto);
        Assert.EndsWith(".xls", abierto);
    }

    [Fact]
    public async Task NoPermitido_seAbreEnElNavegadorSinBajarlo()
    {
        var docs = Con(_ => throw new InvalidOperationException("no debería bajarse"));

        var abierto = await docs.AbrirAsync("https://otro.sitio.com/ayuda");

        Assert.Equal("https://otro.sitio.com/ayuda", abierto);
        Assert.Equal("https://otro.sitio.com/ayuda", Assert.Single(_abiertos));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("Documents/relativa.aspx")]
    public async Task SoloHttp_loDemasSeRechaza(string direccion)
    {
        var docs = Con(_ => throw new InvalidOperationException("no debería bajarse"));

        Assert.Null(await docs.AbrirAsync(direccion));
        Assert.Empty(_abiertos);
    }

    [Fact]
    public void Cookies_formatoPlaywright()
    {
        var cookies = Lanzamiento.LeerCookies("""
            [{"name":"ASP.NET_SessionId","value":"abc","domain":"servicios.ejemplo.edu.ar","path":"/","httpOnly":true,"secure":false},
             {"name":"x","value":"1","domain":".ejemplo.edu.ar","path":"/App"}]
            """);

        Assert.Equal(2, cookies.Count);
        Assert.True(cookies[0].HttpOnly);
        Assert.Equal("/App", cookies[1].Path);
    }
}
