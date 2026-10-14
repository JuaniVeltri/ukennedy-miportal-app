// SPDX-License-Identifier: MIT
using Plata.Host;
using Xunit;

public class ArranqueTests
{
    [Fact]
    public void Resolver_rutaAnidada_respetaRaiz()
    {
        var raiz = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(raiz, "_framework"));
        File.WriteAllText(Path.Combine(raiz, "_framework", "blazor.webassembly.js"), "x");
        var s = new ServidorDeArchivos(raiz);
        using var st = s.Resolver("plata://app/_framework/blazor.webassembly.js", out var ct);
        Assert.NotNull(st);
        Assert.Equal("text/javascript", ct);
    }
}
