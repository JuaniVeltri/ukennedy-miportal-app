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

        if (!File.Exists(destino)) return null;
        contentType = TiposDeContenido.Para(destino);
        return File.OpenRead(destino);
    }
}
