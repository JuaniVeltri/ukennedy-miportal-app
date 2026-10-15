// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;

namespace Plata.Host;

/// <summary>
/// Ajustes del navegador de la ventana que Photino no ofrece (Linux, WebKitGTK). Se llaman desde el
/// hilo de la ventana, una vez creada. En otros sistemas no hacen nada.
/// </summary>
internal static class WebKitGtk
{
    private const string WebKit = "libwebkit2gtk-4.1.so.0";
    private const string Gtk = "libgtk-3.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string GLib = "libglib-2.0.so.0";

    /// <summary>
    /// Un esquema propio cuenta como inseguro: una página https no puede cargarlo en un iframe
    /// (contenido mixto) y el enlace nunca llega a su manejador. Se lo registra como seguro.
    /// </summary>
    public static void MarcarEsquemaComoSeguro(string esquema)
        => Intentar(() => webkit_security_manager_register_uri_scheme_as_secure(
            webkit_web_context_get_security_manager(webkit_web_context_get_default()), esquema));

    /// <summary>
    /// El portal corre en un marco dentro de la ventana de Plata: para el navegador, sus cookies
    /// son "de terceros" y por defecto no se guardan todas. Sin ellas no hay sesión.
    /// </summary>
    public static void AceptarCookiesDeTerceros()
        => Intentar(() => webkit_cookie_manager_set_accept_policy(webkit_website_data_manager_get_cookie_manager(
            webkit_web_context_get_website_data_manager(webkit_web_context_get_default())), 0 /* ACCEPT_ALWAYS */));

    /// <summary>Agrega un script que corre en cada página y marco de la ventana (al terminar de cargar).</summary>
    public static void AgregarScript(string titulo, string fuente)
        => Intentar(() =>
        {
            var vista = BuscarVista(titulo) ?? throw new InvalidOperationException("no se encontró la vista de la ventana");
            var script = webkit_user_script_new(fuente, 0 /* ALL_FRAMES */, 1 /* DOCUMENT_END */, IntPtr.Zero, IntPtr.Zero);
            webkit_user_content_manager_add_script(webkit_web_view_get_user_content_manager(vista), script);
            webkit_user_script_unref(script);
        });

    // Photino no da la vista en Linux: se la busca dentro de la ventana con ese título.
    private static IntPtr? BuscarVista(string titulo)
    {
        var lista = gtk_window_list_toplevels();
        try
        {
            for (var nodo = lista; nodo != IntPtr.Zero; nodo = Marshal.ReadIntPtr(nodo, IntPtr.Size))
            {
                var ventana = Marshal.ReadIntPtr(nodo);
                if (Marshal.PtrToStringUTF8(gtk_window_get_title(ventana)) != titulo) continue;
                var hijo = gtk_bin_get_child(ventana);
                if (hijo != IntPtr.Zero && Marshal.PtrToStringUTF8(g_type_name_from_instance(hijo)) == "WebKitWebView") return hijo;
            }
            return null;
        }
        finally { g_list_free(lista); }
    }

    private static void Intentar(Action a)
    {
        if (!OperatingSystem.IsLinux()) return;
        try { a(); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            Console.WriteLine("ajuste del navegador no aplicado: " + e.Message);
        }
    }

    [DllImport(WebKit)] private static extern IntPtr webkit_web_context_get_default();
    [DllImport(WebKit)] private static extern IntPtr webkit_web_context_get_security_manager(IntPtr contexto);
    [DllImport(WebKit)] private static extern void webkit_security_manager_register_uri_scheme_as_secure(IntPtr gestor, string esquema);
    [DllImport(WebKit)] private static extern IntPtr webkit_web_context_get_website_data_manager(IntPtr contexto);
    [DllImport(WebKit)] private static extern IntPtr webkit_website_data_manager_get_cookie_manager(IntPtr datos);
    [DllImport(WebKit)] private static extern void webkit_cookie_manager_set_accept_policy(IntPtr gestor, int politica);
    [DllImport(WebKit)] private static extern IntPtr webkit_web_view_get_user_content_manager(IntPtr vista);
    [DllImport(WebKit)] private static extern IntPtr webkit_user_script_new(string fuente, int marcos, int momento, IntPtr permitidas, IntPtr bloqueadas);
    [DllImport(WebKit)] private static extern void webkit_user_content_manager_add_script(IntPtr gestor, IntPtr script);
    [DllImport(WebKit)] private static extern void webkit_user_script_unref(IntPtr script);
    [DllImport(Gtk)] private static extern IntPtr gtk_window_list_toplevels();
    [DllImport(Gtk)] private static extern IntPtr gtk_window_get_title(IntPtr ventana);
    [DllImport(Gtk)] private static extern IntPtr gtk_bin_get_child(IntPtr contenedor);
    [DllImport(GObject)] private static extern IntPtr g_type_name_from_instance(IntPtr instancia);
    [DllImport(GLib)] private static extern void g_list_free(IntPtr lista);
}
