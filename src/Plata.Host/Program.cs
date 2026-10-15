// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Photino.NET;
using Plata.Host;

var motor = new ServidorDeArchivos(ArgOf(args, "--motor") ?? Path.Combine(AppContext.BaseDirectory, "motor"));
// Red de las apps: solo los dominios permitidos (separados por coma), p. ej. "kennedy.edu.ar".
var portero = new Portero((ArgOf(args, "--permitir") ?? "").Split(','));
// Lo que las apps abren en otra ventana (reportes): lo baja el Portero y lo abre el sistema.
var documentos = new Documentos(portero.Permitido, portero.TraerDocumentoAsync,
    a => Process.Start(new ProcessStartInfo(a) { UseShellExecute = true }),
    Path.Combine(Path.GetTempPath(), "plata-documentos"));
var diagnostico = args.Contains("--diagnostico");

// Portal: una ventana con pestañas. La primera es el sitio desde el que se abren las apps (p. ej.
// MiPortal); el usuario inicia sesión ahí. Cuando el portal entrega el enlace de una app (uksv:),
// Plata hace lo que hacía el visor de Windows y la abre en una pestaña nueva.
if (ArgOf(args, "--portal") is { } portal)
    AbrirPortal(new Uri(portal));
else
    AbrirApp();
return;

// Una sola app, sin portal: desde una carpeta lanzada por una herramienta externa (desarrollo)
// o con los argumentos sueltos (--xap, --init-params, --origen, --pagina).
void AbrirApp()
{
    var lanzamiento = ArgOf(args, "--app") is { } carpeta ? Lanzamiento.DesdeCarpeta(carpeta) : null;
    if (lanzamiento is not null)
        Console.WriteLine($"app {lanzamiento.NombreXap}: {portero.AgregarCookies(lanzamiento.Cookies)} cookies de sesión");
    var deLanzamiento = lanzamiento is not null ? SesionDeApp.Para(lanzamiento, motor, portero, documentos) : null;
    // Los argumentos sueltos reemplazan a lo que diga el lanzamiento.
    var sesion = new SesionDeApp(motor, portero, documentos)
    {
        Xap = ArgOf(args, "--xap") ?? deLanzamiento?.Xap,
        InitParams = ArgOf(args, "--init-params") is { } f ? File.ReadAllText(f).Trim() : deLanzamiento?.InitParams ?? "",
        Origen = ArgOf(args, "--origen") is { } o ? new Uri(o.EndsWith('/') ? o : o + "/") : deLanzamiento?.Origen,
        Pagina = ArgOf(args, "--pagina") ?? deLanzamiento?.Pagina ?? "",
        Lanzamiento = lanzamiento,
        Diagnostico = diagnostico,
    };

    PhotinoWindow? win = null;
    win = new PhotinoWindow()
        .SetTitle(lanzamiento?.Titulo is { Length: > 0 } t ? "Plata · " + t : "Plata")
        .SetUseOsDefaultSize(false)
        .SetSize(1280, 800)
        .Center()
        .SetLogVerbosity(0)
        .RegisterCustomSchemeHandler("plata", (object _, string _, string url, out string ct)
            => sesion.Resolver(url, out ct) ?? Stream.Null)
        .RegisterWebMessageReceivedHandler((_, m) => sesion.Atender(m, r => win!.SendWebMessage(r)))
        .Load(new Uri(sesion.Direccion("app")));
    win.WaitForClose();
}

void AbrirPortal(Uri inicio)
{
    const string Titulo = "Plata";
    // Los mensajes de la ventana llevan esta marca: solo la página de la ventana la conoce (los
    // marcos del portal y de las apps son de otro origen y no pueden leerla).
    var marca = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    var html = Recurso("ventana.html")
        .Replace("__TOKEN__", marca)
        .Replace("__PORTAL_TITULO__", JavaScriptEncoder.Default.Encode(ArgOf(args, "--portal-nombre") ?? "Portal"))
        .Replace("__PORTAL__", JavaScriptEncoder.Default.Encode(inicio.AbsoluteUri));
    // Scripts que corren en el portal: el que le dice que el visor está instalado y, para
    // desarrollo, uno local opcional (p. ej. completar el inicio de sesión en las pruebas).
    var scripts = Lanzador.ScriptDelPortal + (ArgOf(args, "--script-dev") is { } dev ? "\n" + File.ReadAllText(dev) : "");
    var lanzador = new Lanzador(portero);
    var sesiones = new ConcurrentDictionary<string, SesionDeApp>();
    var idPorXap = new ConcurrentDictionary<string, string>();
    var lanzados = new HashSet<string>();

    PhotinoWindow? win = null;
    void AVentana(object o) => win!.SendWebMessage("plata-ventana:" + JsonSerializer.Serialize(o));

    win = new PhotinoWindow()
        .SetTitle(Titulo)
        .SetUseOsDefaultSize(false)
        .SetSize(1280, 840)
        .Center()
        .SetLogVerbosity(0)
        // El portal solo ofrece las apps a Windows (el visor oficial es de Windows). La universidad
        // autorizó presentarse como Windows ante el portal (docs/autorizacion.md).
        .SetUserAgent("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36")
        .RegisterCustomSchemeHandler("plata", (object _, string _, string url, out string ct) =>
        {
            var host = new Uri(url).Host;
            if (host == "plata")
            {
                ct = "text/html";
                return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html));
            }
            if (sesiones.TryGetValue(host, out var s)) return s.Resolver(url, out ct) ?? Stream.Null;
            ct = "text/plain";
            return Stream.Null;
        })
        .RegisterCustomSchemeHandler(Lanzador.Esquema, (object _, string _, string url, out string ct) =>
        {
            // El portal pone el enlace en un iframe oculto: se atiende aparte para no trabar la ventana.
            // Las cookies del portal se leen acá (hilo de la ventana): el lanzador las manda como el navegador.
            bool nuevo;
            lock (lanzados) nuevo = lanzados.Add(url);
            if (nuevo && lanzador.Direccion(url) is { } destino)
            {
                var cookies = CookiesDelNavegador.ParaAsync(destino);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        AVentana(new { tipo = "abriendo" });
                        var delNavegador = await cookies;
                        Console.WriteLine($"cookies del portal: {string.Join(" ", delNavegador.Select(c => c.Name + "@" + c.Domain + (c.Secure ? "(segura)" : "")))}; " +
                                          $"agregadas {portero.AgregarCookies(delNavegador)}");
                        var l = await lanzador.LanzarAsync(url);
                        var id = idPorXap.GetOrAdd(l.NombreXap, _ => "app" + (idPorXap.Count + 1));
                        var sesion = SesionDeApp.Para(l, motor, portero, documentos, diagnostico);
                        sesiones[id] = sesion;
                        Console.WriteLine($"lanzada {l.NombreXap} en la pestaña {id} ({l.Titulo})");
                        AVentana(new { tipo = "app", id, titulo = l.TituloCorto, url = sesion.Direccion(id) });
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("lanzador: " + e.Message);
                        AVentana(new { tipo = "error", texto = "No se pudo abrir la aplicación. " + e.Message });
                    }
                });
            }
            ct = "text/html";
            return new MemoryStream("<!doctype html><title>Plata</title>"u8.ToArray());
        })
        .RegisterWebMessageReceivedHandler((_, m) =>
        {
            // De una app: "plata-app:<marca>:<pestaña>:<mensaje>". De la ventana: "plata-ventana:<marca>:<orden>".
            if (SinMarca(m, "plata-app:") is { } resto && resto.IndexOf(':') is var i and > 0)
            {
                var id = resto[..i];
                if (sesiones.TryGetValue(id, out var s)) s.Atender(resto[(i + 1)..], r => win!.SendWebMessage($"plata-app:{id}:{r}"));
            }
            else if (SinMarca(m, "plata-ventana:") is { } orden && orden.StartsWith("cerrar:", StringComparison.Ordinal))
                sesiones.TryRemove(orden["cerrar:".Length..], out SesionDeApp? _);
        })
        .RegisterWindowCreatedHandler((_, _) =>
        {
            WebKitGtk.MarcarEsquemaComoSeguro(Lanzador.Esquema);
            WebKitGtk.AceptarCookiesDeTerceros();
            WebKitGtk.AgregarScript(Titulo, scripts);
        })
        .Load(new Uri("plata://plata/"));
    win.WaitForClose();

    string? SinMarca(string m, string prefijo)
        => m.StartsWith(prefijo + marca + ":", StringComparison.Ordinal) ? m[(prefijo.Length + marca.Length + 1)..] : null;
}

static string Recurso(string nombre)
{
    using var s = typeof(SesionDeApp).Assembly.GetManifestResourceStream(nombre)
        ?? throw new InvalidOperationException("falta el recurso " + nombre);
    return new StreamReader(s).ReadToEnd();
}

static string? ArgOf(string[] a, string n)
{
    var i = Array.IndexOf(a, n);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
}
