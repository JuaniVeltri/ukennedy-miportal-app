// SPDX-License-Identifier: MIT
using Plata.Host;
using Xunit;

public class PoliticaDeAccesoTests
{
    // La que publican los servidores de servicios de la universidad.
    private const string AbiertaHttp = """
        <?xml version="1.0" encoding="utf-8"?>
        <access-policy><cross-domain-access><policy>
          <allow-from http-request-headers="*"><domain uri="http://*"/></allow-from>
          <grant-to><resource path="/" include-subpaths="true"/></grant-to>
        </policy></cross-domain-access></access-policy>
        """;

    private static readonly Uri Xap = new("http://servicios.ejemplo.edu.ar/App/ClientBin/App.xap");
    private static readonly string[] Soap = ["Content-Type", "SOAPAction"];

    private static (PoliticaDeAcceso Politica, List<string> Pedidas) Con(Dictionary<string, string> archivos)
    {
        var pedidas = new List<string>();
        return (new PoliticaDeAcceso(u =>
        {
            pedidas.Add(u.AbsoluteUri);
            return Task.FromResult(archivos.TryGetValue(u.AbsoluteUri, out var t) ? t : null);
        }), pedidas);
    }

    [Fact]
    public async Task MismoSitio_sinPedirPolitica()
    {
        var (politica, pedidas) = Con(new());
        Assert.True(await politica.PermiteAsync(Xap, new Uri("http://servicios.ejemplo.edu.ar/Otra/x.svc"), Soap));
        Assert.Empty(pedidas);
    }

    [Fact]
    public async Task OtroSitio_conPoliticaAbierta_unaSolaVez()
    {
        var (politica, pedidas) = Con(new() { ["http://servicios1.ejemplo.edu.ar/clientaccesspolicy.xml"] = AbiertaHttp });
        Assert.True(await politica.PermiteAsync(Xap, new Uri("http://servicios1.ejemplo.edu.ar/Services/A.svc"), Soap));
        Assert.True(await politica.PermiteAsync(Xap, new Uri("http://servicios1.ejemplo.edu.ar/Services/B.svc"), Soap));
        Assert.Single(pedidas);
    }

    [Fact]
    public async Task OtroSitio_sinPolitica_seRechaza()
    {
        var (politica, pedidas) = Con(new());
        Assert.False(await politica.PermiteAsync(Xap, new Uri("http://otro.com/x.svc"), Soap));
        Assert.Equal(["http://otro.com/clientaccesspolicy.xml", "http://otro.com/crossdomain.xml"], pedidas);
    }

    [Fact]
    public async Task PoliticaSoloHttp_noAbreAUnXapHttps()
    {
        var (politica, _) = Con(new() { ["http://servicios1.ejemplo.edu.ar/clientaccesspolicy.xml"] = AbiertaHttp });
        Assert.False(await politica.PermiteAsync(new Uri("https://servicios.ejemplo.edu.ar/App.xap"),
            new Uri("http://servicios1.ejemplo.edu.ar/x.svc"), Soap));
    }

    [Theory]
    [InlineData("*", "http://a.com/x.xap", true)]
    [InlineData("http://*", "http://a.com/x.xap", true)]
    [InlineData("https://*", "http://a.com/x.xap", false)]
    [InlineData("http://*.ejemplo.edu.ar", "http://servicios.ejemplo.edu.ar/x.xap", true)]
    [InlineData("http://*.ejemplo.edu.ar", "http://ejemplo.edu.ar.malo.com/x.xap", false)]
    [InlineData("http://servicios.ejemplo.edu.ar", "http://servicios.ejemplo.edu.ar/x.xap", true)]
    [InlineData("http://servicios.ejemplo.edu.ar:8080", "http://servicios.ejemplo.edu.ar/x.xap", false)]
    [InlineData("http://otro.edu.ar", "http://servicios.ejemplo.edu.ar/x.xap", false)]
    public void Dominio(string patron, string xap, bool esperado)
        => Assert.Equal(esperado, PoliticaDeAcceso.Politica.DominioPermite(patron, new Uri(xap)));

    [Fact]
    public async Task RecursosYCabeceras_restringidos()
    {
        const string xml = """
            <access-policy><cross-domain-access><policy>
              <allow-from http-request-headers="SOAPAction, X-Plata-*"><domain uri="*"/></allow-from>
              <grant-to><resource path="/Services" include-subpaths="true"/><resource path="/solo.svc"/></grant-to>
            </policy></cross-domain-access></access-policy>
            """;
        var (politica, _) = Con(new() { ["http://s.com/clientaccesspolicy.xml"] = xml });

        Assert.True(await politica.PermiteAsync(Xap, new Uri("http://s.com/Services/A.svc"), Soap));
        Assert.True(await politica.PermiteAsync(Xap, new Uri("http://s.com/solo.svc"), ["X-Plata-Algo"]));
        Assert.False(await politica.PermiteAsync(Xap, new Uri("http://s.com/solo.svc/mas"), Soap));
        Assert.False(await politica.PermiteAsync(Xap, new Uri("http://s.com/ServicesX/A.svc"), Soap));
        Assert.False(await politica.PermiteAsync(Xap, new Uri("http://s.com/Services/A.svc"), ["Authorization"]));
    }

    [Fact]
    public async Task CrossDomain_soloAbiertoATodos()
    {
        var (abierta, _) = Con(new() { ["http://a.com/crossdomain.xml"] =
            """<cross-domain-policy><allow-access-from domain="*"/><allow-http-request-headers-from domain="*" headers="SOAPAction"/></cross-domain-policy>""" });
        var (cerrada, _) = Con(new() { ["http://b.com/crossdomain.xml"] =
            """<cross-domain-policy><allow-access-from domain="*.ejemplo.edu.ar"/></cross-domain-policy>""" });

        Assert.True(await abierta.PermiteAsync(Xap, new Uri("http://a.com/x.svc"), Soap));
        Assert.False(await cerrada.PermiteAsync(Xap, new Uri("http://b.com/x.svc"), Soap));
    }
}
