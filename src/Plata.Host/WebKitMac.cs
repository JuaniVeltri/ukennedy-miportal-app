// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;

namespace Plata.Host;

/// <summary>
/// Ajustes del navegador de la ventana que Photino no ofrece, en macOS (WKWebView). Se hablan con
/// Objective-C desde .NET (objc_msgSend). Se llaman desde el hilo de la ventana, una vez creada.
/// Cada paso se intenta por separado: si uno no está disponible en esta versión de macOS, se
/// registra y se sigue.
/// </summary>
internal static class WebKitMac
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    /// <summary>
    /// Desactiva la prevención de rastreo (ITP) del navegador de la ventana: con ella, WKWebView no
    /// guarda las cookies del portal, que corre en un marco y para el navegador es "de terceros".
    /// Es una opción interna de WebKit (la usa Safari para sus ajustes de desarrollo).
    /// </summary>
    public static void PermitirCookiesDelPortal()
        => Intentar("cookies del portal", () =>
        {
            var datos = Enviar(Configuracion(), Sel("websiteDataStore"));
            var opcion = Sel("_setResourceLoadStatisticsEnabled:");
            if (!EnviarBool(datos, Sel("respondsToSelector:"), opcion))
                throw new InvalidOperationException("esta versión de WebKit no tiene _setResourceLoadStatisticsEnabled:");
            EnviarConBool(datos, opcion, false);
        });

    /// <summary>
    /// Acepta las cookies de todos los sitios, como en Linux (ACCEPT_ALWAYS). Por defecto WKWebView
    /// solo acepta las del dominio del documento principal, que es la ventana (plata://plata/): las
    /// del portal, que corre en un marco, se descartan y el inicio de sesión vuelve sin sesión.
    /// </summary>
    public static void AceptarCookiesDeTerceros()
        => Intentar("aceptar cookies de terceros", () =>
        {
            var almacen = Enviar(Enviar(Configuracion(), Sel("websiteDataStore")), Sel("httpCookieStore"));
            var opcion = Sel("_setCookieAcceptPolicy:completionHandler:");
            if (!EnviarBool(almacen, Sel("respondsToSelector:"), opcion))
                throw new InvalidOperationException("esta versión de WebKit no tiene _setCookieAcceptPolicy:");
            EnviarPolitica(almacen, opcion, 0 /* NSHTTPCookieAcceptPolicyAlways */, BloqueSinRespuesta.Value);
        });

    /// <summary>Agrega un script que corre en cada página y marco de la ventana (al terminar de cargar).</summary>
    public static void AgregarScript(string fuente)
        => Intentar("script del portal", () =>
        {
            var controlador = Enviar(Configuracion(), Sel("userContentController"));
            var script = EnviarScript(Enviar(Clase("WKUserScript"), Sel("alloc")),
                Sel("initWithSource:injectionTime:forMainFrameOnly:"), Texto(fuente), 1 /* AtDocumentEnd */, false);
            Enviar(controlador, Sel("addUserScript:"), script);
        });

    /// <summary>Todas las cookies del navegador de la ventana.</summary>
    public static Task<IReadOnlyList<Cookie>> CookiesAsync()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<Cookie>>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var almacen = Enviar(Enviar(Configuracion(), Sel("websiteDataStore")), Sel("httpCookieStore"));
            Pendientes.Enqueue(tcs);
            Enviar(almacen, Sel("getAllCookies:"), BloqueDeCookies.Value);
        }
        catch (Exception e)
        {
            Console.WriteLine("macOS: no se pudieron leer las cookies del navegador: " + e.Message);
            tcs.TrySetResult([]);
        }
        return tcs.Task;
    }

    // getAllCookies: responde en el hilo de la ventana, en el orden de los pedidos.
    private static readonly ConcurrentQueue<TaskCompletionSource<IReadOnlyList<Cookie>>> Pendientes = new();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void InvocarBloque(IntPtr bloque, IntPtr cookies);

    private static readonly InvocarBloque AlRecibirCookies = (_, arreglo) =>
    {
        var lista = new List<Cookie>();
        try
        {
            for (nint i = 0, n = (nint)EnviarEntero(arreglo, Sel("count")); i < n; i++)
            {
                var c = Enviar(arreglo, Sel("objectAtIndex:"), i);
                var nombre = Cadena(Enviar(c, Sel("name")));
                var dominio = Cadena(Enviar(c, Sel("domain")));
                if (string.IsNullOrEmpty(nombre) || string.IsNullOrEmpty(dominio)) continue;
                try
                {
                    lista.Add(new Cookie(nombre, Cadena(Enviar(c, Sel("value"))) ?? "", Cadena(Enviar(c, Sel("path"))) ?? "/", dominio)
                    {
                        Secure = EnviarBool0(c, Sel("isSecure")),
                        HttpOnly = EnviarBool0(c, Sel("isHTTPOnly")),
                    });
                }
                catch (CookieException e) { Console.WriteLine($"cookie {nombre} ({dominio}) omitida: {e.Message}"); }
            }
        }
        catch (Exception e) { Console.WriteLine("macOS: cookies del navegador: " + e.Message); }
        if (Pendientes.TryDequeue(out var tcs)) tcs.TrySetResult(lista);
    };

    private static readonly Lazy<IntPtr> BloqueDeCookies = new(() => BloqueGlobal(AlRecibirCookies));

    // WebKit llama siempre al bloque de respuesta de _setCookieAcceptPolicy: (no admite nil).
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void InvocarBloqueVacio(IntPtr bloque);

    private static readonly InvocarBloqueVacio AlAplicarPolitica = _ => Console.WriteLine("macOS: aceptar cookies de terceros: aplicado");
    private static readonly Lazy<IntPtr> BloqueSinRespuesta = new(() => BloqueGlobal(AlAplicarPolitica));

    // Un bloque de Objective-C global (no se copia ni se libera) que llama al delegado, que tiene que
    // seguir vivo (los de esta clase son estáticos).
    // Estructura: isa, flags, reservado, invoke, descriptor; descriptor: reservado, tamaño.
    private static IntPtr BloqueGlobal(Delegate invocar)
    {
        var descriptor = Marshal.AllocHGlobal(16);
        Marshal.WriteInt64(descriptor, 0, 0);
        Marshal.WriteInt64(descriptor, 8, 32);
        var bloque = Marshal.AllocHGlobal(32);
        Marshal.WriteIntPtr(bloque, 0, NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_NSConcreteGlobalBlock"));
        Marshal.WriteInt32(bloque, 8, 1 << 28); // BLOCK_IS_GLOBAL
        Marshal.WriteInt32(bloque, 12, 0);
        Marshal.WriteIntPtr(bloque, 16, Marshal.GetFunctionPointerForDelegate(invocar));
        Marshal.WriteIntPtr(bloque, 24, descriptor);
        return bloque;
    }

    // La configuración de la vista de la ventana (WKWebView, dentro del contentView de la ventana).
    private static IntPtr Configuracion()
    {
        var ventanas = Enviar(Enviar(Clase("NSApplication"), Sel("sharedApplication")), Sel("windows"));
        var claseVista = Clase("WKWebView");
        for (nint i = 0, n = (nint)EnviarEntero(ventanas, Sel("count")); i < n; i++)
        {
            var vista = Buscar(Enviar(Enviar(ventanas, Sel("objectAtIndex:"), i), Sel("contentView")), claseVista, 0);
            if (vista != IntPtr.Zero) return Enviar(vista, Sel("configuration"));
        }
        throw new InvalidOperationException("no se encontró la vista de la ventana");
    }

    private static IntPtr Buscar(IntPtr vista, IntPtr clase, int nivel)
    {
        if (vista == IntPtr.Zero || nivel > 6) return IntPtr.Zero;
        if (EnviarBool(vista, Sel("isKindOfClass:"), clase)) return vista;
        var hijas = Enviar(vista, Sel("subviews"));
        for (nint i = 0, n = (nint)EnviarEntero(hijas, Sel("count")); i < n; i++)
        {
            var encontrada = Buscar(Enviar(hijas, Sel("objectAtIndex:"), i), clase, nivel + 1);
            if (encontrada != IntPtr.Zero) return encontrada;
        }
        return IntPtr.Zero;
    }

    private static void Intentar(string que, Action a)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try { a(); Console.WriteLine($"macOS: {que}: listo"); }
        catch (Exception e) { Console.WriteLine($"macOS: {que}: no aplicado: {e.Message}"); }
    }

    private static IntPtr Clase(string nombre) => objc_getClass(nombre) is var c && c != IntPtr.Zero ? c
        : throw new InvalidOperationException("no existe la clase " + nombre);

    private static IntPtr Sel(string nombre) => sel_registerName(nombre);

    // NSString (retenido: WebKit lo copia o lo guarda).
    private static IntPtr Texto(string s)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(s);
        try { return Enviar(Enviar(Clase("NSString"), Sel("alloc")), Sel("initWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    private static string? Cadena(IntPtr nsString)
        => nsString == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Enviar(nsString, Sel("UTF8String")));

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string nombre);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string nombre);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Enviar(IntPtr receptor, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Enviar(IntPtr receptor, IntPtr selector, IntPtr argumento);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nuint EnviarEntero(IntPtr receptor, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool EnviarBool(IntPtr receptor, IntPtr selector, IntPtr argumento);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool EnviarBool0(IntPtr receptor, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void EnviarConBool(IntPtr receptor, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool valor);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void EnviarPolitica(IntPtr receptor, IntPtr selector, nuint politica, IntPtr bloque);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr EnviarScript(IntPtr receptor, IntPtr selector, IntPtr fuente, nint momento, [MarshalAs(UnmanagedType.I1)] bool soloPrincipal);
}
