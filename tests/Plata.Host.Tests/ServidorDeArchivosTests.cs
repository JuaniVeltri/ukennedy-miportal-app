// SPDX-License-Identifier: MIT
using Plata.Host;
using Xunit;

public class ServidorDeArchivosTests
{
    [Theory]
    [InlineData("a/b.html", "text/html")]
    [InlineData("x.js", "text/javascript")]
    [InlineData("x.wasm", "application/wasm")]
    [InlineData("x.desconocido", "application/octet-stream")]
    public void TipoDeContenido_porExtension(string ruta, string esperado)
        => Assert.Equal(esperado, TiposDeContenido.Para(ruta));

    [Fact]
    public void Resolver_archivoExistente_devuelveStream()
    {
        var raiz = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(raiz, "index.html"), "<p>hola</p>");
        var s = new ServidorDeArchivos(raiz);
        using var st = s.Resolver("plata://app/index.html", out var ct);
        Assert.NotNull(st);
        Assert.Equal("text/html", ct);
    }

    [Fact]
    public void Resolver_travesiaDeDirectorios_devuelveNull()
    {
        var raiz = Directory.CreateTempSubdirectory().FullName;
        var s = new ServidorDeArchivos(raiz);
        using var st = s.Resolver("plata://app/../../etc/passwd", out _);
        Assert.Null(st);
    }

    [Fact]
    public void Resolver_diferenteMayusculas_encuentraArchivo()
    {
        // En Linux/macOS el disco distingue mayúsculas; el motor pide "opensilver.js"
        // pero el archivo publicado es "OpenSilver.js".
        var raiz = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(raiz, "libs"));
        File.WriteAllText(Path.Combine(raiz, "libs", "OpenSilver.js"), "x");
        var s = new ServidorDeArchivos(raiz);
        using var st = s.Resolver("plata://app/libs/opensilver.js", out var ct);
        Assert.NotNull(st);
        Assert.Equal("text/javascript", ct);
    }

    [Fact]
    public void Resolver_raiz_devuelveIndex()
    {
        var raiz = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(raiz, "index.html"), "<html></html>");
        var s = new ServidorDeArchivos(raiz);
        using var st = s.Resolver("plata://app/", out var ct);
        Assert.NotNull(st);
        Assert.Equal("text/html", ct);
    }
}
