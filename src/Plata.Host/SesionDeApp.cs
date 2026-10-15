// SPDX-License-Identifier: MIT
using System.Text;

namespace Plata.Host;

/// <summary>
/// Una app abierta: atiende los pedidos del motor (archivos, InitParams, origen, página, el .xap)
/// y sus mensajes (red por el Portero, documentos). Cada app se sirve en su propio origen
/// (<c>plata://&lt;id&gt;/</c>), así varias apps conviven en la misma ventana sin mezclarse.
/// </summary>
public sealed class SesionDeApp
{
    public const string Red = "plata-net:";
    public const string Abrir = "plata-abrir:";
    private const string RutaDeOrigen = "/resources/plata-origen/";

    private readonly ServidorDeArchivos _motor;
    private readonly Portero _portero;
    private readonly Documentos _documentos;

    public SesionDeApp(ServidorDeArchivos motor, Portero portero, Documentos documentos)
    {
        _motor = motor;
        _portero = portero;
        _documentos = documentos;
    }

    /// <summary>El .xap a cargar: nombre de archivo (relativo al motor o al lanzamiento).</summary>
    public string? Xap { get; init; }

    /// <summary>InitParams ("clave=valor,clave=valor", como el &lt;param&gt; de la página original).</summary>
    public string InitParams { get; init; } = "";

    /// <summary>Carpeta de origen del .xap (p. ej. ".../ClientBin/"): ahí se buscan sus rutas relativas.</summary>
    public Uri? Origen { get; init; }

    /// <summary>La página que contenía la app: contra ella se resuelven las direcciones que abre.</summary>
    public string Pagina { get; init; } = "";

    public Lanzamiento? Lanzamiento { get; init; }
    public bool Diagnostico { get; init; }

    public static SesionDeApp Para(Lanzamiento l, ServidorDeArchivos motor, Portero portero, Documentos documentos, bool diagnostico = false)
        => new(motor, portero, documentos)
        {
            Xap = l.NombreXap,
            InitParams = l.InitParams,
            Origen = new Uri(l.Xap, "."),
            Pagina = l.Pagina?.AbsoluteUri ?? "",
            Lanzamiento = l,
            Diagnostico = diagnostico,
        };

    /// <summary>La dirección con la que se abre el motor para esta app.</summary>
    public string Direccion(string id) => $"plata://{id}/" + (Xap is not null ? "?xap=" + Uri.EscapeDataString(Xap) : "");

    public Stream? Resolver(string url, out string contentType)
    {
        var ruta = new Uri(url).AbsolutePath;
        string? texto = ruta switch
        {
            // Se sirven por el esquema y no por la URL: Photino aborta con URLs largas y así
            // tampoco quedan en el historial de navegación.
            "/_plata/init-params" => InitParams,
            "/_plata/diagnostico" => Diagnostico ? "1" : "",
            "/_plata/rastrear" => "", // el rastreo de métodos es para el arnés de desarrollo
            "/_plata/origen" => Origen?.AbsoluteUri ?? "",
            "/_plata/pagina" => Pagina,
            _ => null,
        };
        if (texto is not null)
        {
            contentType = "text/plain";
            return new MemoryStream(Encoding.UTF8.GetBytes(texto));
        }
        if (Lanzamiento is { } l && ruta == "/" + l.NombreXap)
        {
            contentType = "application/x-silverlight-app";
            if (l.ArchivoXap is { } archivo) return File.OpenRead(archivo);
            var (bytes, _) = _portero.TraerAsync(l.Xap).GetAwaiter().GetResult();
            return bytes is null ? null : new MemoryStream(bytes);
        }
        if (Origen is not null && ruta.StartsWith(RutaDeOrigen, StringComparison.Ordinal))
        {
            var (datos, tipo) = _portero.TraerAsync(new Uri(Origen, ruta[RutaDeOrigen.Length..])).GetAwaiter().GetResult();
            contentType = tipo;
            return datos is null ? null : new MemoryStream(datos);
        }
        return _motor.Resolver(url, out contentType);
    }

    /// <summary>Atiende un mensaje del motor; las respuestas salen por <paramref name="responder"/>.</summary>
    public void Atender(string mensaje, Action<string> responder)
    {
        if (mensaje.StartsWith(Red, StringComparison.Ordinal))
        {
            _ = Task.Run(async () => responder(Red + await _portero.AtenderAsync(mensaje[Red.Length..])));
            return;
        }
        if (mensaje.StartsWith(Abrir, StringComparison.Ordinal))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var abierto = await _documentos.AbrirAsync(mensaje[Abrir.Length..]);
                    Console.WriteLine(abierto is null ? "abrir: rechazado (solo http/https)" : "abrir: " + Path.GetFileName(new Uri(abierto).AbsolutePath));
                }
                catch (Exception e) { Console.WriteLine("abrir: " + e.GetType().Name + ": " + e.Message); }
            });
            return;
        }
        Console.WriteLine("MSG " + mensaje);
    }
}
