// SPDX-License-Identifier: MIT
using System.Text;

namespace Plata.Host;

/// <summary>Un documento bajado por el Portero: contenido, tipo y la dirección final (tras redirecciones).</summary>
public sealed record Documento(byte[] Datos, string Tipo, string? NombreSugerido, Uri Final);

/// <summary>
/// Abre lo que la app pide abrir en otra ventana (reportes, comprobantes: en Silverlight,
/// <c>HtmlPage.Window.Navigate(uri, "_blank")</c>). Si el dominio está permitido, el documento
/// se baja por el Portero (con la sesión de la app) y se abre con el programa del sistema; si no,
/// la dirección se abre en el navegador del sistema. Solo se aceptan direcciones http/https.
/// </summary>
public sealed class Documentos
{
    private readonly Func<Uri, bool> _permitido;
    private readonly Func<Uri, Task<Documento?>> _traer;
    private readonly Action<string> _abrirConElSistema;
    private readonly string _carpeta;

    public Documentos(Func<Uri, bool> permitido, Func<Uri, Task<Documento?>> traer,
                      Action<string> abrirConElSistema, string carpeta)
    {
        _permitido = permitido;
        _traer = traer;
        _abrirConElSistema = abrirConElSistema;
        _carpeta = carpeta;
    }

    /// <summary>Abre la dirección. Devuelve lo que se abrió (archivo local o dirección), o null si se rechazó.</summary>
    public async Task<string?> AbrirAsync(string direccion)
    {
        if (!Uri.TryCreate(direccion, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            return null;
        if (!_permitido(url))
        {
            _abrirConElSistema(url.AbsoluteUri);
            return url.AbsoluteUri;
        }

        var doc = await _traer(url);
        if (doc is null)
        {
            _abrirConElSistema(url.AbsoluteUri);
            return url.AbsoluteUri;
        }

        var extension = Extension(doc);
        var datos = doc.Datos;
        // Una página guardada pierde su dirección: <base> hace que sus estilos e imágenes
        // relativos se sigan pidiendo al sitio.
        if (extension == ".html") datos = ConBase(datos, doc.Final);

        Directory.CreateDirectory(_carpeta);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_carpeta, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var archivo = Path.Combine(_carpeta, $"{Path.GetFileNameWithoutExtension(Seguro(doc.NombreSugerido) ?? "documento")}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}");
        await File.WriteAllBytesAsync(archivo, datos);
        _abrirConElSistema(archivo);
        return archivo;
    }

    internal static string Extension(Documento doc)
    {
        var tipo = doc.Tipo.Split(';')[0].Trim().ToLowerInvariant();
        var deTipo = tipo switch
        {
            "application/pdf" => ".pdf",
            "text/html" or "application/xhtml+xml" => ".html",
            "application/vnd.ms-excel" => ".xls",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            "application/msword" => ".doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "text/plain" => ".txt",
            "text/csv" => ".csv",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            _ => null,
        };
        if (deTipo is not null) return deTipo;
        var deNombre = Path.GetExtension(Seguro(doc.NombreSugerido) ?? doc.Final.AbsolutePath);
        return deNombre is { Length: > 1 and <= 6 } && !deNombre.Equals(".aspx", StringComparison.OrdinalIgnoreCase)
            ? deNombre.ToLowerInvariant() : ".bin";
    }

    private static string? Seguro(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return null;
        var n = Path.GetFileName(nombre.Trim().Trim('"'));
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        return n.Length == 0 ? null : n;
    }

    private static byte[] ConBase(byte[] html, Uri final)
    {
        // Latin1 ida y vuelta conserva cada byte, sea cual sea la codificación de la página.
        var texto = Encoding.Latin1.GetString(html);
        var etiqueta = $"<base href=\"{System.Net.WebUtility.HtmlEncode(final.AbsoluteUri)}\">";
        var head = texto.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        var cierre = head >= 0 ? texto.IndexOf('>', head) : -1;
        texto = cierre >= 0 ? texto.Insert(cierre + 1, etiqueta) : etiqueta + texto;
        return Encoding.Latin1.GetBytes(texto);
    }
}
