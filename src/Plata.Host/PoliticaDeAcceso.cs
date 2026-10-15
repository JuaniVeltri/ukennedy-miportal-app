// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Xml.Linq;

namespace Plata.Host;

/// <summary>
/// Las reglas de Silverlight para la red de una app. Puede llamar sin más a su sitio de origen
/// (mismo esquema, host y puerto que el .xap); a otro sitio, solo si ese sitio lo permite en su
/// <c>/clientaccesspolicy.xml</c> o, si no lo tiene, en un <c>/crossdomain.xml</c> abierto a todos.
/// La política de cada sitio se pide una vez.
/// </summary>
public sealed class PoliticaDeAcceso
{
    private readonly Func<Uri, Task<string?>> _traer;
    private readonly ConcurrentDictionary<string, Task<Politica>> _porSitio = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="traer">Trae un archivo de política (null si no existe o no se pudo).</param>
    public PoliticaDeAcceso(Func<Uri, Task<string?>> traer) => _traer = traer;

    public async Task<bool> PermiteAsync(Uri xap, Uri destino, IReadOnlyCollection<string> cabeceras)
    {
        if (Sitio(xap) == Sitio(destino)) return true;
        var politica = await _porSitio.GetOrAdd(Sitio(destino), _ => ObtenerAsync(destino));
        return politica.Permite(xap, destino, cabeceras);
    }

    private static string Sitio(Uri u) => u.GetLeftPart(UriPartial.Authority).ToLowerInvariant();

    private async Task<Politica> ObtenerAsync(Uri destino)
    {
        var raiz = new Uri(destino.GetLeftPart(UriPartial.Authority) + "/");
        try
        {
            if (await _traer(new Uri(raiz, "clientaccesspolicy.xml")) is { } cap) return Politica.DeClientAccessPolicy(cap);
            if (await _traer(new Uri(raiz, "crossdomain.xml")) is { } cd) return Politica.DeCrossDomain(cd);
        }
        catch (Exception e) when (e is System.Xml.XmlException or HttpRequestException)
        {
            Console.WriteLine($"política de {raiz.Host} ilegible: {e.Message}");
        }
        return Politica.Ninguna;
    }

    internal sealed record Regla(string[] Dominios, string[] Cabeceras, (string Ruta, bool Subrutas)[] Recursos);

    internal sealed class Politica(IReadOnlyList<Regla> reglas)
    {
        public static readonly Politica Ninguna = new([]);

        // Cabeceras que la app siempre puede mandar.
        private static readonly string[] Simples = ["Content-Type", "Content-Length", "Accept"];

        public bool Permite(Uri xap, Uri destino, IReadOnlyCollection<string> cabeceras)
            => reglas.Any(r => r.Dominios.Any(d => DominioPermite(d, xap))
                               && r.Recursos.Any(x => RecursoPermite(x.Ruta, x.Subrutas, destino.AbsolutePath))
                               && cabeceras.All(c => Simples.Contains(c, StringComparer.OrdinalIgnoreCase)
                                                     || r.Cabeceras.Any(p => CabeceraPermite(p, c))));

        // <access-policy><cross-domain-access><policy><allow-from http-request-headers="…"><domain uri="…"/>
        // </allow-from><grant-to><resource path="/" include-subpaths="true"/></grant-to></policy>…
        public static Politica DeClientAccessPolicy(string xml)
            => new(XDocument.Parse(xml).Descendants("policy").Select(p =>
            {
                var desde = p.Elements("allow-from").ToList();
                return new Regla(
                    desde.SelectMany(a => a.Elements("domain")).Select(d => (string?)d.Attribute("uri") ?? "").ToArray(),
                    desde.SelectMany(a => Lista((string?)a.Attribute("http-request-headers"))).ToArray(),
                    p.Elements("grant-to").Elements("resource")
                        .Select(r => ((string?)r.Attribute("path") ?? "", string.Equals((string?)r.Attribute("include-subpaths"), "true", StringComparison.OrdinalIgnoreCase)))
                        .ToArray());
            }).ToList());

        // Silverlight solo acepta un crossdomain.xml abierto a todos: <allow-access-from domain="*"/>.
        public static Politica DeCrossDomain(string xml)
        {
            var raiz = XDocument.Parse(xml).Root;
            if (raiz is null || !raiz.Elements("allow-access-from").Any(a => (string?)a.Attribute("domain") == "*")) return Ninguna;
            var cabeceras = raiz.Elements("allow-http-request-headers-from")
                .Where(a => (string?)a.Attribute("domain") == "*")
                .SelectMany(a => Lista((string?)a.Attribute("headers"))).ToArray();
            return new([new Regla(["*"], cabeceras, [("/", true)])]);
        }

        private static IEnumerable<string> Lista(string? valor)
            => (valor ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // "*" (cualquiera), "http://*" / "https://*" (cualquiera con ese esquema),
        // "http://*.ejemplo.com" (subdominios) o "http://ejemplo.com[:puerto]" (ese sitio).
        internal static bool DominioPermite(string patron, Uri xap)
        {
            if (patron == "*") return true;
            var i = patron.IndexOf("://", StringComparison.Ordinal);
            if (i <= 0 || !patron[..i].Equals(xap.Scheme, StringComparison.OrdinalIgnoreCase)) return false;
            var resto = patron[(i + 3)..].TrimEnd('/');
            if (resto == "*") return true;
            if (resto.StartsWith("*.", StringComparison.Ordinal))
                return xap.Host.EndsWith(resto[1..], StringComparison.OrdinalIgnoreCase);
            return Uri.TryCreate(patron, UriKind.Absolute, out var sitio)
                   && sitio.Host.Equals(xap.Host, StringComparison.OrdinalIgnoreCase) && sitio.Port == xap.Port;
        }

        internal static bool RecursoPermite(string ruta, bool subrutas, string pedida)
        {
            if (ruta.Length == 0) return false;
            if (string.Equals(ruta, pedida, StringComparison.OrdinalIgnoreCase)) return true;
            if (!subrutas) return false;
            var prefijo = ruta.EndsWith('/') ? ruta : ruta + "/";
            return pedida.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase);
        }

        // "*", un nombre, o un prefijo con comodín al final ("X-Plata-*").
        internal static bool CabeceraPermite(string patron, string cabecera)
            => patron == "*"
               || (patron.EndsWith('*') && cabecera.StartsWith(patron[..^1], StringComparison.OrdinalIgnoreCase))
               || patron.Equals(cabecera, StringComparison.OrdinalIgnoreCase);
    }
}
