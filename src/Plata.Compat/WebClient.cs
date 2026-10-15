// SPDX-License-Identifier: MIT
using System.ComponentModel;

namespace Plata.Compat;

/// <summary>De dónde vino la app: Silverlight resuelve las direcciones relativas contra el .xap.</summary>
public static class Origen
{
    /// <summary>La dirección real del .xap (en Silverlight, <c>Application.Current.Host.Source</c>).</summary>
    public static Uri? Xap { get; set; }

    /// <summary>La página que contenía la app (en Silverlight, <c>HtmlPage.Document.DocumentUri</c>).</summary>
    public static Uri? Pagina { get; set; }

    internal static Uri Absoluta(Uri direccion)
        => direccion.IsAbsoluteUri ? direccion
            : new Uri(Xap ?? throw new InvalidOperationException($"Plata: dirección relativa '{direccion}' sin origen del .xap."), direccion);
}

/// <summary>Carga de librerías que la app trae en tiempo de ejecución (<c>AssemblyPart.Load</c>).</summary>
public static class Ensamblados
{
    /// <summary>Lo conecta el motor: traduce la librería y la carga; null si no hace falta cargarla.</summary>
    public static Func<byte[], System.Reflection.Assembly?>? Cargar { get; set; }
}

/// <summary>
/// Reemplazo del <c>WebClient</c> de Silverlight: baja por el <see cref="Portero"/> (el navegador
/// bloquearía las llamadas a los servidores de la institución) y resuelve las direcciones
/// relativas contra el .xap. Cubre lo que usan las apps: <c>OpenReadAsync</c> con sus eventos.
/// </summary>
public class WebClient
{
    private CancellationTokenSource? _cancelar;

    public event OpenReadCompletedEventHandler? OpenReadCompleted;
    public event DownloadProgressChangedEventHandler? DownloadProgressChanged;

    public bool IsBusy => _cancelar is not null;

    public void OpenReadAsync(Uri address) => OpenReadAsync(address, null);

    public void OpenReadAsync(Uri address, object? userToken)
    {
        if (IsBusy) throw new NotSupportedException("WebClient does not support concurrent I/O operations.");
        var cancelar = _cancelar = new CancellationTokenSource();
        _ = BajarAsync(address, userToken, cancelar);
    }

    public void CancelAsync() => _cancelar?.Cancel();

    private async Task BajarAsync(Uri direccion, object? token, CancellationTokenSource cancelar)
    {
        Stream? resultado = null;
        Exception? error = null;
        try
        {
            await Task.Yield(); // como en Silverlight: el resultado llega siempre después de OpenReadAsync
            var datos = await TraerAsync(Origen.Absoluta(direccion));
            resultado = new MemoryStream(datos);
            DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedEventArgs(datos.Length, datos.Length, token));
        }
        catch (Exception e) { error = e; }
        _cancelar = null;
        OpenReadCompleted?.Invoke(this, new OpenReadCompletedEventArgs(resultado, error, cancelar.IsCancellationRequested, token));
    }

    private static async Task<byte[]> TraerAsync(Uri url)
    {
        if (Portero.Enviar is null)
        {
            using var http = new HttpClient();
            return await http.GetByteArrayAsync(url);
        }
        var r = await Portero.Enviar(new PedidoHttp("GET", url.AbsoluteUri, Array.Empty<(string, string)>(), Array.Empty<byte>()));
        if (r.Estado is < 200 or > 299)
            throw new System.Net.WebException($"The remote server returned an error: {r.Estado} ({url.GetLeftPart(UriPartial.Path)}).");
        return r.Cuerpo;
    }
}

public delegate void OpenReadCompletedEventHandler(object sender, OpenReadCompletedEventArgs e);

public class OpenReadCompletedEventArgs(Stream? resultado, Exception? error, bool cancelado, object? estado)
    : AsyncCompletedEventArgs(error, cancelado, estado)
{
    public Stream Result
    {
        get
        {
            RaiseExceptionIfNecessary();
            return resultado!;
        }
    }
}

public delegate void DownloadProgressChangedEventHandler(object sender, DownloadProgressChangedEventArgs e);

public class DownloadProgressChangedEventArgs(long recibidos, long total, object? estado)
    : ProgressChangedEventArgs(total > 0 ? (int)(recibidos * 100 / total) : 0, estado)
{
    public long BytesReceived { get; } = recibidos;
    public long TotalBytesToReceive { get; } = total;
}
