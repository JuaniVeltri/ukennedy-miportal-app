// SPDX-License-Identifier: MIT
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plata.Host;

/// <summary>
/// Hace la red de las apps (diseño, §4.8). El motor, encerrado en el navegador, manda cada
/// pedido HTTP como mensaje; acá se hace la conexión real y se devuelve la respuesta.
/// Solo se atienden dominios permitidos de forma explícita (y sus subdominios). Las cookies
/// viven solo en memoria. No se registran direcciones con parámetros ni contenidos.
/// </summary>
public sealed class Portero
{
    private readonly string[] _dominios;
    private readonly HttpClient _http;
    private readonly CookieContainer _cookies = new();

    public Portero(IEnumerable<string> dominiosPermitidos)
    {
        _dominios = dominiosPermitidos.Select(d => d.Trim().TrimStart('.').ToLowerInvariant())
                                      .Where(d => d.Length > 0).ToArray();
        _http = new HttpClient(new SocketsHttpHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
        })
        { Timeout = TimeSpan.FromMinutes(2) };
    }

    public bool Permitido(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps) return false;
        var host = url.IdnHost.ToLowerInvariant();
        return _dominios.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }

    /// <summary>Atiende un pedido (JSON) y devuelve la respuesta (JSON). Nunca lanza.</summary>
    public async Task<string> AtenderAsync(string pedidoJson)
    {
        Pedido? p = null;
        try
        {
            p = JsonSerializer.Deserialize<Pedido>(pedidoJson, Json)!;
            var url = new Uri(p.Url);
            if (!Permitido(url))
                return Responder(p.Id, error: $"dominio no permitido: {url.Host}");

            using var pedido = new HttpRequestMessage(new HttpMethod(p.Metodo), url);
            if (p.Cuerpo is { Length: > 0 }) pedido.Content = new ByteArrayContent(Convert.FromBase64String(p.Cuerpo));
            foreach (var c in p.Cabeceras ?? [])
            {
                if (c.Length != 2) continue;
                if (pedido.Content is not null && c[0].StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                    pedido.Content.Headers.TryAddWithoutValidation(c[0], c[1]);
                else
                    pedido.Headers.TryAddWithoutValidation(c[0], c[1]);
            }

            using var r = await _http.SendAsync(pedido);
            var cuerpo = await r.Content.ReadAsByteArrayAsync();
            var cabeceras = r.Headers.Concat(r.Content.Headers)
                .SelectMany(h => h.Value.Select(v => new[] { h.Key, v })).ToArray();
            Console.WriteLine($"red {p.Metodo} {url.GetLeftPart(UriPartial.Path)} -> {(int)r.StatusCode}, {cuerpo.Length} bytes");
            return Responder(p.Id, (int)r.StatusCode, cabeceras, cuerpo);
        }
        catch (Exception e)
        {
            return Responder(p?.Id ?? 0, error: e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>
    /// Trae un archivo (p. ej. una imagen relativa al .xap) del sitio de origen.
    /// Devuelve null si el dominio no está permitido o si la respuesta no es exitosa.
    /// </summary>
    public async Task<(byte[]? Datos, string Tipo)> TraerAsync(Uri url)
    {
        if (!Permitido(url)) return (null, "text/plain");
        try
        {
            using var r = await _http.GetAsync(url);
            if (!r.IsSuccessStatusCode) return (null, "text/plain");
            var tipo = r.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            return (await r.Content.ReadAsByteArrayAsync(), tipo);
        }
        catch (HttpRequestException) { return (null, "text/plain"); }
    }

    /// <summary>
    /// Baja un documento que la app pide abrir (ver <see cref="Documentos"/>). Null si el dominio
    /// no está permitido, si la respuesta no es exitosa o si no hay red.
    /// </summary>
    public async Task<Documento?> TraerDocumentoAsync(Uri url)
    {
        if (!Permitido(url)) return null;
        try
        {
            using var r = await _http.GetAsync(url);
            if (!r.IsSuccessStatusCode) return null;
            var final = r.RequestMessage?.RequestUri ?? url;
            Console.WriteLine($"documento {final.GetLeftPart(UriPartial.Path)} -> {(int)r.StatusCode}, {r.Content.Headers.ContentType}");
            return new Documento(await r.Content.ReadAsByteArrayAsync(),
                r.Content.Headers.ContentType?.ToString() ?? "application/octet-stream",
                r.Content.Headers.ContentDisposition?.FileNameStar ?? r.Content.Headers.ContentDisposition?.FileName, final);
        }
        catch (HttpRequestException) { return null; }
    }

    /// <summary>
    /// Pide una página (siguiendo redirecciones HTTP) y devuelve su estado, su texto y la dirección
    /// final. Lanza si el dominio no está permitido o si no hay red.
    /// </summary>
    public async Task<(int Estado, string Html, Uri Final)> TraerPaginaAsync(Uri url)
    {
        if (!Permitido(url)) throw new InvalidOperationException("dominio no permitido: " + url.Host);
        using var r = await _http.GetAsync(url);
        return ((int)r.StatusCode, await r.Content.ReadAsStringAsync(), r.RequestMessage?.RequestUri ?? url);
    }

    /// <summary>Para el registro: nombres (sin valores) de las cookies que se mandarían a la dirección.</summary>
    public string NombresDeCookiesPara(Uri url) => string.Join(" ", _cookies.GetCookies(url).Select(c => c.Name));

    /// <summary>Las cookies de la sesión (para pasársela a la ventana de una app).</summary>
    public IReadOnlyList<Cookie> Cookies() => _cookies.GetAllCookies().ToList();

    /// <summary>
    /// Agrega cookies de una sesión ya iniciada (p. ej. la del portal de la universidad), solo de
    /// los dominios permitidos. Devuelve cuántas se agregaron.
    /// </summary>
    public int AgregarCookies(IEnumerable<Cookie> cookies)
    {
        var n = 0;
        foreach (var c in cookies)
        {
            var dominio = c.Domain.TrimStart('.');
            if (!Permitido(new Uri("https://" + dominio + "/"))) continue;
            _cookies.Add(c);
            n++;
        }
        return n;
    }

    private static string Responder(int id, int estado = 0, string[][]? cabeceras = null, byte[]? cuerpo = null, string? error = null)
        => JsonSerializer.Serialize(new Respuesta(id, estado, cabeceras ?? [], Convert.ToBase64String(cuerpo ?? []), error), Json);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record Pedido(int Id, string Metodo, string Url, string[][]? Cabeceras, string? Cuerpo);

    private sealed record Respuesta(
        int Id, int Estado, string[][] Cabeceras, string Cuerpo,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error);
}
