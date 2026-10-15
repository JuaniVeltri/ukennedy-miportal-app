// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Text;
using Mono.Cecil;
using Plata.Compat;
using Plata.Motor.Puente;
using Xunit;

[Collection("Portero")] // Portero.Enviar y Origen.Xap son globales
public class PaquetesTests
{
    [Fact]
    public void LeerDelPaquete_devuelveLaEntradaSinDistinguirMayusculas()
    {
        var zip = Zip(("AppManifest.xaml", "<Deployment/>"), ("Modulo.dll", "binario"));
        var info = new System.Windows.Resources.StreamResourceInfo(zip, null!);

        var manifiesto = Desvios.LeerDelPaquete(info, new Uri("appmanifest.xaml", UriKind.Relative));
        var modulo = Desvios.LeerDelPaquete(info, new Uri("Modulo.dll", UriKind.Relative));

        Assert.Equal("<Deployment/>", new StreamReader(manifiesto!.Stream).ReadToEnd());
        Assert.Equal("binario", new StreamReader(modulo!.Stream).ReadToEnd());
        Assert.Null(Desvios.LeerDelPaquete(info, new Uri("NoEsta.dll", UriKind.Relative)));
    }

    [Fact]
    public async Task WebClient_relativaAlXap_bajaPorElPorteroYAvisa()
    {
        var pedidas = new List<string>();
        // La respuesta espera a que la prueba vea la descarga en curso (si no, puede terminar antes).
        var responder = new TaskCompletionSource();
        Portero.Enviar = async p =>
        {
            pedidas.Add(p.Metodo + " " + p.Url);
            await responder.Task;
            return new RespuestaHttp(200, Array.Empty<(string, string)>(), Encoding.UTF8.GetBytes("paquete"));
        };
        Origen.Xap = new Uri("http://servicios.ejemplo.edu.ar/App/ClientBin/Principal.xap");
        try
        {
            var wc = new Plata.Compat.WebClient();
            var listo = new TaskCompletionSource<OpenReadCompletedEventArgs>();
            wc.OpenReadCompleted += (_, e) => listo.SetResult(e);
            wc.OpenReadAsync(new Uri("Modulos/Inscripciones.xap", UriKind.Relative));
            Assert.True(wc.IsBusy);
            responder.SetResult();

            var e = await listo.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(e.Error);
            Assert.Equal("paquete", new StreamReader(e.Result).ReadToEnd());
            Assert.False(wc.IsBusy);
            Assert.Equal("GET http://servicios.ejemplo.edu.ar/App/ClientBin/Modulos/Inscripciones.xap", Assert.Single(pedidas));
        }
        finally { Portero.Enviar = null; Origen.Xap = null; }
    }

    [Fact]
    public async Task WebClient_errorHttp_quedaEnError()
    {
        Portero.Enviar = _ => Task.FromResult(new RespuestaHttp(404, Array.Empty<(string, string)>(), Array.Empty<byte>()));
        try
        {
            var wc = new Plata.Compat.WebClient();
            var listo = new TaskCompletionSource<OpenReadCompletedEventArgs>();
            wc.OpenReadCompleted += (_, e) => listo.SetResult(e);
            wc.OpenReadAsync(new Uri("http://servicios.ejemplo.edu.ar/x.xap"));

            var e = await listo.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsType<System.Net.WebException>(e.Error);
            Assert.ThrowsAny<Exception>(() => e.Result);
        }
        finally { Portero.Enviar = null; }
    }

    [Fact]
    public void CargarEnsamblado_yaCargadoOProvisto_noSeDuplica()
    {
        var cargador = new CargadorDeApp();
        var a = cargador.CargarEnsamblado(Ensamblado("Plata.PruebaModulo"));
        var b = cargador.CargarEnsamblado(Ensamblado("Plata.PruebaModulo"));

        Assert.NotNull(a);
        Assert.Same(a, b);
        Assert.Null(cargador.CargarEnsamblado(Ensamblado("System.Windows.Controls"))); // lo provee OpenSilver
        Assert.Null(cargador.CargarEnsamblado(Ensamblado("System.Windows.Controls.resources"))); // recursos de idioma
    }

    private static byte[] Ensamblado(string nombre)
    {
        var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(nombre, new Version(1, 0, 0, 0)), nombre, ModuleKind.Dll);
        using var ms = new MemoryStream();
        asm.Write(ms);
        return ms.ToArray();
    }

    private static MemoryStream Zip(params (string Nombre, string Contenido)[] entradas)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (n, c) in entradas)
            {
                using var w = new StreamWriter(zip.CreateEntry(n).Open());
                w.Write(c);
            }
        ms.Position = 0;
        return ms;
    }
}

[Collection("Portero")]
public class VentanasTests
{
    [Fact]
    public void Navegar_relativa_seResuelveContraLaPaginaYLaAbreElHost()
    {
        var abiertas = new List<Uri>();
        Plata.Compat.Ventanas.Abrir = abiertas.Add;
        Plata.Compat.Origen.Pagina = new Uri("http://servicios.ejemplo.edu.ar/App/Gestion.aspx");
        try
        {
            var resultado = Plata.Compat.Desvios.NavegarEn(null!, new Uri("Documents/Estado.aspx?L=1", UriKind.Relative), "_blank");

            Assert.Null(resultado);
            Assert.Equal("http://servicios.ejemplo.edu.ar/App/Documents/Estado.aspx?L=1", Assert.Single(abiertas).AbsoluteUri);
        }
        finally { Plata.Compat.Ventanas.Abrir = null; Plata.Compat.Origen.Pagina = null; }
    }
}
