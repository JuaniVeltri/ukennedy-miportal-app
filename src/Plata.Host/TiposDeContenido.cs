// SPDX-License-Identifier: MIT
namespace Plata.Host;

/// <summary>Asigna un content-type a cada archivo del motor según su extensión.</summary>
public static class TiposDeContenido
{
    private static readonly Dictionary<string, string> Mapa = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html", [".js"] = "text/javascript", [".mjs"] = "text/javascript",
        [".css"] = "text/css", [".wasm"] = "application/wasm", [".json"] = "application/json",
        [".ico"] = "image/x-icon", [".png"] = "image/png", [".svg"] = "image/svg+xml",
        [".woff"] = "font/woff", [".woff2"] = "font/woff2", [".ttf"] = "font/ttf",
        [".dat"] = "application/octet-stream", [".blat"] = "application/octet-stream",
        [".pdb"] = "application/octet-stream", [".dll"] = "application/octet-stream",
    };

    public static string Para(string ruta)
        => Mapa.TryGetValue(Path.GetExtension(ruta), out var ct) ? ct : "application/octet-stream";
}
