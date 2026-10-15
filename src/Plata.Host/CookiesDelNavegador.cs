// SPDX-License-Identifier: MIT
using System.Net;
using System.Runtime.InteropServices;

namespace Plata.Host;

/// <summary>
/// Lee las cookies que el portal dejó en el navegador de la ventana (WebKitGTK en Linux, WKWebView
/// en macOS): la sesión del alumno viaja en ellas y el lanzador tiene que mandarlas como lo haría el
/// navegador. Hay que llamarlo desde el hilo de la ventana.
/// </summary>
public static class CookiesDelNavegador
{
    private const string WebKit = "libwebkit2gtk-4.1.so.0";
    private const string Soup = "libsoup-3.0.so.0";
    private const string GLib = "libglib-2.0.so.0";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Listo(IntPtr origen, IntPtr resultado, IntPtr datos);

    // El delegado tiene que seguir vivo mientras WebKit lo pueda llamar.
    private static readonly Listo AlTerminar = Terminar;
    private static readonly Dictionary<IntPtr, TaskCompletionSource<IReadOnlyList<Cookie>>> Pendientes = new();
    private static long _siguiente;

    /// <summary>
    /// Las cookies que el navegador mandaría a <paramref name="url"/> (en macOS, todas: el Portero
    /// solo acepta las de dominios permitidos). Vacío en otros sistemas.
    /// </summary>
    public static Task<IReadOnlyList<Cookie>> ParaAsync(Uri url)
    {
        if (OperatingSystem.IsMacOS()) return WebKitMac.CookiesAsync();
        if (!OperatingSystem.IsLinux()) return Task.FromResult<IReadOnlyList<Cookie>>([]);
        var tcs = new TaskCompletionSource<IReadOnlyList<Cookie>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = (IntPtr)Interlocked.Increment(ref _siguiente);
        lock (Pendientes) Pendientes[id] = tcs;
        try
        {
            var gestor = webkit_website_data_manager_get_cookie_manager(
                webkit_web_context_get_website_data_manager(webkit_web_context_get_default()));
            webkit_cookie_manager_get_cookies(gestor, url.AbsoluteUri, IntPtr.Zero, AlTerminar, id);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            lock (Pendientes) Pendientes.Remove(id);
            Console.WriteLine("no se pudieron leer las cookies del navegador: " + e.Message);
            return Task.FromResult<IReadOnlyList<Cookie>>([]);
        }
        return tcs.Task;
    }

    private static void Terminar(IntPtr gestor, IntPtr resultado, IntPtr id)
    {
        TaskCompletionSource<IReadOnlyList<Cookie>>? tcs;
        lock (Pendientes) Pendientes.Remove(id, out tcs);
        var cookies = new List<Cookie>();
        try
        {
            var lista = webkit_cookie_manager_get_cookies_finish(gestor, resultado, out var error);
            if (error != IntPtr.Zero) g_error_free(error);
            for (var nodo = lista; nodo != IntPtr.Zero; nodo = Marshal.ReadIntPtr(nodo, IntPtr.Size)) // GList: data, next, prev
            {
                var c = Marshal.ReadIntPtr(nodo);
                var nombre = Marshal.PtrToStringUTF8(soup_cookie_get_name(c));
                var dominio = Marshal.PtrToStringUTF8(soup_cookie_get_domain(c));
                if (string.IsNullOrEmpty(nombre) || string.IsNullOrEmpty(dominio)) continue;
                try
                {
                    cookies.Add(new Cookie(nombre, Marshal.PtrToStringUTF8(soup_cookie_get_value(c)) ?? "",
                        Marshal.PtrToStringUTF8(soup_cookie_get_path(c)) ?? "/", dominio)
                    { Secure = soup_cookie_get_secure(c), HttpOnly = soup_cookie_get_http_only(c) });
                }
                catch (CookieException e) { Console.WriteLine($"cookie {nombre} ({dominio}) omitida: {e.Message}"); }
            }
            if (lista != IntPtr.Zero)
                g_list_free_full(lista, NativeLibrary.GetExport(NativeLibrary.Load(Soup), "soup_cookie_free"));
        }
        catch (Exception e) { Console.WriteLine("cookies del navegador: " + e.Message); }
        tcs?.TrySetResult(cookies);
    }

    [DllImport(WebKit)] private static extern IntPtr webkit_web_context_get_default();
    [DllImport(WebKit)] private static extern IntPtr webkit_web_context_get_website_data_manager(IntPtr contexto);
    [DllImport(WebKit)] private static extern IntPtr webkit_website_data_manager_get_cookie_manager(IntPtr datos);
    [DllImport(WebKit)] private static extern void webkit_cookie_manager_get_cookies(IntPtr gestor, string uri, IntPtr cancelable, Listo listo, IntPtr datos);
    [DllImport(WebKit)] private static extern IntPtr webkit_cookie_manager_get_cookies_finish(IntPtr gestor, IntPtr resultado, out IntPtr error);
    [DllImport(Soup)] private static extern IntPtr soup_cookie_get_name(IntPtr cookie);
    [DllImport(Soup)] private static extern IntPtr soup_cookie_get_value(IntPtr cookie);
    [DllImport(Soup)] private static extern IntPtr soup_cookie_get_domain(IntPtr cookie);
    [DllImport(Soup)] private static extern IntPtr soup_cookie_get_path(IntPtr cookie);
    [DllImport(Soup)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool soup_cookie_get_secure(IntPtr cookie);
    [DllImport(Soup)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool soup_cookie_get_http_only(IntPtr cookie);
    [DllImport(GLib)] private static extern void g_list_free_full(IntPtr lista, IntPtr liberar);
    [DllImport(GLib)] private static extern void g_error_free(IntPtr error);
}
