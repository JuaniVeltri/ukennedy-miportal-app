// SPDX-License-Identifier: MIT
namespace Plata.Host;

/// <summary>
/// Sirve los archivos del motor desde el disco, mapeando una URL del esquema
/// <c>plata://app/&lt;ruta&gt;</c> a un archivo bajo <paramref name="raiz"/>.
/// No levanta ningún servidor HTTP. Rechaza rutas que se escapan de la raíz.
/// </summary>
public sealed class ServidorDeArchivos(string raiz)
{
    private readonly string _raiz = Path.GetFullPath(raiz);

    public Stream? Resolver(string url, out string contentType)
    {
        contentType = "text/plain";
        var uri = new Uri(url);
        var rel = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (rel.Length == 0) rel = "index.html";

        var destino = Path.GetFullPath(Path.Combine(_raiz, rel));
        // Defensa contra travesía de directorios: el destino debe quedar dentro de la raíz.
        if (destino != _raiz &&
            !destino.StartsWith(_raiz + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return null;

        destino = ResolverSinDistinguirMayusculas(destino);
        if (destino is null) return null;
        contentType = TiposDeContenido.Para(destino);
        return File.OpenRead(destino);
    }

    // Linux/macOS distinguen mayúsculas en el disco; el motor pide archivos con
    // una capitalización distinta a la publicada (p. ej. "opensilver.js" vs "OpenSilver.js").
    private static string? ResolverSinDistinguirMayusculas(string destino)
    {
        if (File.Exists(destino)) return destino;
        var dir = Path.GetDirectoryName(destino);
        if (dir is null || !Directory.Exists(dir)) return null;
        var nombre = Path.GetFileName(destino);
        foreach (var f in Directory.EnumerateFiles(dir))
            if (string.Equals(Path.GetFileName(f), nombre, StringComparison.OrdinalIgnoreCase))
                return f;
        return null;
    }
}
