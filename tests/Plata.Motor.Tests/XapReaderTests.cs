// SPDX-License-Identifier: MIT
using System.IO.Compression;
using Plata.Motor.Xap;
using Xunit;

public class XapReaderTests
{
    private static Stream HacerXap(string manifest, params (string, byte[])[] extra)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(z.CreateEntry("AppManifest.xaml").Open()))
                w.Write(manifest);
            foreach (var (n, b) in extra)
                using (var e = z.CreateEntry(n).Open()) e.Write(b);
        }
        ms.Position = 0;
        return ms;
    }

    private const string ManifestOk = """
        <Deployment xmlns="http://schemas.microsoft.com/client/2007/deployment"
                    EntryPointAssembly="MiApp" EntryPointType="MiApp.App" RuntimeVersion="5.0.0.0">
          <Deployment.Parts>
            <AssemblyPart x:Name="MiApp" Source="MiApp.dll" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
          </Deployment.Parts>
        </Deployment>
        """;

    [Fact]
    public void Leer_manifiestoValido_devuelveCampos()
    {
        var pkg = XapReader.Leer(HacerXap(ManifestOk, ("MiApp.dll", new byte[] { 1, 2, 3 })));
        Assert.Equal("MiApp", pkg.Manifest.EntryPointAssembly);
        Assert.Equal("MiApp.App", pkg.Manifest.EntryPointType);
        Assert.Single(pkg.Manifest.Parts);
        Assert.Equal(new byte[] { 1, 2, 3 }, pkg.Assembly("MiApp"));
    }

    [Fact]
    public void Leer_noEsZip_lanzaXapInvalido()
    {
        var basura = new MemoryStream("no soy un zip"u8.ToArray());
        Assert.Throws<PlataXapInvalidoException>(() => XapReader.Leer(basura));
    }

    [Fact]
    public void Leer_manifiestoSinEntryPoint_lanzaConCampo()
    {
        var manifest = """
            <Deployment xmlns="http://schemas.microsoft.com/client/2007/deployment" RuntimeVersion="5.0.0.0">
              <Deployment.Parts />
            </Deployment>
            """;
        var ex = Assert.Throws<PlataXapInvalidoException>(() => XapReader.Leer(HacerXap(manifest)));
        Assert.Contains("EntryPointAssembly", ex.Message);
    }

    [Fact]
    public void Leer_sinManifiesto_lanza()
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            using (var e = z.CreateEntry("otra.txt").Open()) e.Write("x"u8);
        ms.Position = 0;
        var ex = Assert.Throws<PlataXapInvalidoException>(() => XapReader.Leer(ms));
        Assert.Contains("AppManifest", ex.Message);
    }
}
