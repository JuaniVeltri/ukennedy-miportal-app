// SPDX-License-Identifier: MIT
using System.Net;
using System.Text.Json;

namespace Plata.Host;

/// <summary>
/// Lo que hace falta para abrir una app tal como la abría su página web: la dirección real del
/// .xap, la página que lo contenía, los InitParams y la sesión (cookies) del sitio. Si
/// <paramref name="ArchivoXap"/> es null, el .xap se baja de <paramref name="Xap"/> por el Portero.
/// </summary>
public sealed record Lanzamiento(Uri Xap, Uri? Pagina, string InitParams, IReadOnlyList<Cookie> Cookies,
                                 string? ArchivoXap = null, string? Titulo = null)
{
    public string NombreXap => Path.GetFileName(Xap.AbsolutePath);

    /// <summary>
    /// El nombre para mostrar: del título de la página, la parte que nombra la app
    /// ("Universidad Kennedy - UK Académico - 2" → "UK Académico"); si no hay, el del .xap.
    /// </summary>
    public string TituloCorto => (Titulo ?? "").Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Where(p => !p.All(char.IsDigit)).LastOrDefault() ?? Path.GetFileNameWithoutExtension(NombreXap);

    /// <summary>
    /// Lee una app ya lanzada desde el portal por una herramienta externa (desarrollo): carpeta con
    /// el .xap, <c>xap-url.txt</c>, <c>pagina.txt</c>, <c>initparams.txt</c> y, en la carpeta de
    /// arriba, <c>cookies.json</c>.
    /// </summary>
    public static Lanzamiento DesdeCarpeta(string carpeta)
    {
        string? Leer(string n) => File.Exists(Path.Combine(carpeta, n)) ? File.ReadAllText(Path.Combine(carpeta, n)).Trim() : null;
        var xap = new Uri(Leer("xap-url.txt") ?? throw new FileNotFoundException("falta xap-url.txt", carpeta));
        var archivo = Path.Combine(carpeta, Path.GetFileName(xap.AbsolutePath));
        if (!File.Exists(archivo)) throw new FileNotFoundException("falta el .xap", archivo);
        var cookies = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(carpeta).TrimEnd(Path.DirectorySeparatorChar))!, "cookies.json");
        return new Lanzamiento(xap,
            Leer("pagina.txt") is { Length: > 0 } p ? new Uri(p) : null,
            Leer("initparams.txt") ?? "",
            File.Exists(cookies) ? LeerCookies(File.ReadAllText(cookies)) : [],
            archivo);
    }

    /// <summary>Cookies en el formato de Playwright/DevTools: [{name, value, domain, path, secure, httpOnly}].</summary>
    public static IReadOnlyList<Cookie> LeerCookies(string json)
    {
        var lista = new List<Cookie>();
        foreach (var c in JsonDocument.Parse(json).RootElement.EnumerateArray())
        {
            string? S(string n) => c.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            bool B(string n) => c.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
            if (S("name") is not { Length: > 0 } nombre || S("domain") is not { Length: > 0 } dominio) continue;
            lista.Add(new Cookie(nombre, S("value") ?? "", S("path") ?? "/", dominio) { Secure = B("secure"), HttpOnly = B("httpOnly") });
        }
        return lista;
    }
}
