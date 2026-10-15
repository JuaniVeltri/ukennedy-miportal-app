// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Photino.NET;
using Plata.Host;

// Una app empaquetada trae sus argumentos en argumentos.txt, al lado del ejecutable: abierta desde el
// Finder o el menú no recibe ninguno. Los de la línea de comandos van primero y mandan.
if (Path.Combine(AppContext.BaseDirectory, "argumentos.txt") is var archivoDeArgumentos && File.Exists(archivoDeArgumentos))
    args = args.Concat(File.ReadAllLines(archivoDeArgumentos).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))).ToArray();
// Registro en un archivo (además de la consola): sin consola, es lo único que queda para ver qué pasó.
if (ArgOf(args, "--registro") is { } registro) Registro.Iniciar(registro);

var motor = new ServidorDeArchivos(ArgOf(args, "--motor") is { } m ? Ruta(m) : Path.Combine(AppContext.BaseDirectory, "motor"));
// Red de las apps: solo los dominios permitidos (separados por coma), p. ej. "kennedy.edu.ar".
var portero = new Portero((ArgOf(args, "--permitir") ?? "").Split(','));
// Lo que las apps abren en otra ventana (reportes): lo baja el Portero y lo abre el sistema.
var documentos = new Documentos(portero.Permitido, portero.TraerDocumentoAsync,
    a => Process.Start(new ProcessStartInfo(a) { UseShellExecute = true }),
    Path.Combine(Path.GetTempPath(), "plata-documentos"));
var diagnostico = args.Contains("--diagnostico");
// Cómo se presenta la app (título de la ventana, marca de la barra) y dónde está su código fuente.
// Las apps empaquetadas los fijan en su lanzador; sin ellos, es Plata.
var nombre = ArgOf(args, "--nombre") ?? "Plata";
// Ícono de la ventana (relativo al ejecutable si no es una ruta completa).
var icono = ArgOf(args, "--icono") is { } ic && Ruta(ic) is var ri && File.Exists(ri) ? ri : null;
var repositorio = ArgOf(args, "--repo") is { } r && Uri.TryCreate(r, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u : null;
// La red de las apps sigue las reglas de Silverlight (clientaccesspolicy.xml).
var politica = new PoliticaDeAcceso(portero.TraerPoliticaAsync);

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
    var sesion = new SesionDeApp(motor, portero, documentos, politica)
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
        .SetTitle(lanzamiento?.Titulo is { Length: > 0 } t ? nombre + " · " + t : nombre)
        .SetUseOsDefaultSize(false)
        .SetSize(1280, 800)
        .Center()
        .SetLogVerbosity(0)
        .RegisterCustomSchemeHandler("plata", (object _, string _, string url, out string ct)
            => sesion.Resolver(url, out ct) ?? Stream.Null)
        .RegisterWebMessageReceivedHandler((_, m) => sesion.Atender(m, r => win!.SendWebMessage(r)))
        .Load(new Uri(sesion.Direccion("app")));
    if (icono is not null) win.SetIconFile(icono);
    win.WaitForClose();
}

void AbrirPortal(Uri inicio)
{
    var titulo = nombre;
    // "Nombre (aclaración)": la aclaración va aparte en la barra, como etiqueta.
    var parentesis = nombre.EndsWith(')') ? nombre.LastIndexOf(" (", StringComparison.Ordinal) : -1;
    // Los mensajes de la ventana llevan esta marca: solo la página de la ventana la conoce (los
    // marcos del portal y de las apps son de otro origen y no pueden leerla).
    var marca = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    var html = Recurso("ventana.html")
        .Replace("__TOKEN__", marca)
        .Replace("__NOMBRE__", System.Net.WebUtility.HtmlEncode(nombre))
        .Replace("__MARCA__", System.Net.WebUtility.HtmlEncode(parentesis > 0 ? nombre[..parentesis] : nombre))
        .Replace("__ETIQUETA__", System.Net.WebUtility.HtmlEncode(parentesis > 0 ? nombre[(parentesis + 2)..^1] : ""))
        .Replace("__CON_REPO__", repositorio is null ? "false" : "true")
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
        .SetTitle(titulo)
        .SetUseOsDefaultSize(false)
        .SetSize(1280, 840)
        .Center()
        .SetLogVerbosity(0)
        // El portal solo ofrece las apps a Windows (el visor oficial es de Windows). La universidad
        // autorizó presentarse como Windows ante el portal.
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
        // El enlace llega por dos caminos: el script del portal lo avisa apenas lo pone en su iframe, y
        // en Linux el iframe además lo carga por este esquema. Se lanza una sola vez.
        .RegisterCustomSchemeHandler(Lanzador.Esquema, (object _, string _, string url, out string ct) =>
        {
            Lanzar(url, "esquema");
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
            else if (SinMarca(m, "plata-ventana:") is { } orden)
            {
                if (orden.StartsWith("cerrar:", StringComparison.Ordinal))
                    sesiones.TryRemove(orden["cerrar:".Length..], out SesionDeApp? _);
                else if (orden == "codigo" && repositorio is not null)
                    Process.Start(new ProcessStartInfo(repositorio.AbsoluteUri) { UseShellExecute = true });
                else if (orden.StartsWith("uksv:", StringComparison.Ordinal))
                    Lanzar(orden, "script del portal");
                else if (orden.StartsWith("cuenta:", StringComparison.Ordinal))
                    AtenderCuenta(orden["cuenta:".Length..]);
            }
        })
        .RegisterWindowCreatedHandler((_, _) => Navegador.Preparar(titulo, scripts, Lanzador.Esquema))
        .Load(new Uri("plata://plata/"));
    if (icono is not null) win.SetIconFile(icono);
    win.WaitForClose();

    // Abre la app del enlace en una pestaña. Se llama en el hilo de la ventana: ahí se leen las
    // cookies del portal, que el lanzador manda como lo haría el navegador.
    void Lanzar(string url, string via)
    {
        bool nuevo;
        lock (lanzados) nuevo = lanzados.Add(url);
        if (!nuevo || lanzador.Direccion(url) is not { } destino) return;
        Console.WriteLine("enlace de app recibido por " + via);
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
                var sesion = SesionDeApp.Para(l, motor, portero, documentos, politica, diagnostico);
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

    // "Recordarme" del portal: la cuenta va al llavero del sistema (ver Credenciales), fuera del hilo
    // de la ventana. El registro nunca lleva el usuario ni la contraseña.
    void AtenderCuenta(string orden) => Task.Run(() =>
    {
        var sitio = inicio.Host;
        try
        {
            switch (orden)
            {
                case "consultar":
                    AVentana(new { tipo = "cuenta", disponible = Credenciales.Disponible, usuario = Credenciales.Disponible ? Credenciales.Leer(sitio)?.Usuario : null });
                    break;
                case "clave":
                    var cuenta = Credenciales.Leer(sitio);
                    AVentana(new { tipo = "cuenta-clave", usuario = cuenta?.Usuario, clave = cuenta?.Clave });
                    break;
                case "olvidar":
                    Credenciales.Olvidar(sitio);
                    Console.WriteLine("cuenta recordada: borrada");
                    break;
                default:
                    if (!orden.StartsWith("guardar:", StringComparison.Ordinal)) return;
                    var o = JsonSerializer.Deserialize<JsonElement>(orden["guardar:".Length..]);
                    var (usuario, clave) = (o.GetProperty("usuario").GetString(), o.GetProperty("clave").GetString());
                    if (string.IsNullOrEmpty(usuario) || string.IsNullOrEmpty(clave)) return;
                    Credenciales.Guardar(sitio, new Cuenta(usuario, clave));
                    Console.WriteLine("cuenta recordada: guardada en el llavero");
                    break;
            }
        }
        catch (Exception e) { Console.WriteLine("cuenta recordada: " + e.Message); }
    });

    string? SinMarca(string m, string prefijo)
        => m.StartsWith(prefijo + marca + ":", StringComparison.Ordinal) ? m[(prefijo.Length + marca.Length + 1)..] : null;
}

static string Recurso(string nombre)
{
    using var s = typeof(SesionDeApp).Assembly.GetManifestResourceStream(nombre)
        ?? throw new InvalidOperationException("falta el recurso " + nombre);
    return new StreamReader(s).ReadToEnd();
}

// Una ruta relativa de los argumentos: desde el directorio actual si existe ahí; si no, desde el
// ejecutable (las de argumentos.txt, p. ej. "../Resources/motor" dentro del .app de macOS).
static string Ruta(string r)
    => Path.IsPathRooted(r) || File.Exists(r) || Directory.Exists(r) ? r : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, r));

static string? ArgOf(string[] a, string n)
{
    var i = Array.IndexOf(a, n);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
}
