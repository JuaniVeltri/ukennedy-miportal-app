// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Plata.Host;

/// <summary>Usuario y contraseña recordados para un sitio.</summary>
public sealed record Cuenta(string Usuario, string Clave);

/// <summary>
/// "Recordarme": guarda la cuenta del portal en el llavero del sistema, nunca en un archivo de la
/// app. En Linux, el servicio de secretos del escritorio (GNOME Keyring, KWallet) con libsecret; en
/// macOS, el Llavero. Todo queda en la computadora. En otros sistemas no está disponible.
/// </summary>
public static class Credenciales
{
    private const string Aplicacion = "ukennedy-miportal-app";

    public static bool Disponible => OperatingSystem.IsMacOS() || (OperatingSystem.IsLinux() && Linux.Disponible);

    public static void Guardar(string sitio, Cuenta cuenta)
    {
        var datos = JsonSerializer.Serialize(new { usuario = cuenta.Usuario, clave = cuenta.Clave });
        if (OperatingSystem.IsMacOS()) Mac.Guardar(sitio, datos);
        else if (OperatingSystem.IsLinux()) Linux.Guardar(sitio, datos);
    }

    public static Cuenta? Leer(string sitio)
    {
        var datos = OperatingSystem.IsMacOS() ? Mac.Leer(sitio) : OperatingSystem.IsLinux() ? Linux.Leer(sitio) : null;
        if (datos is null) return null;
        try
        {
            var o = JsonDocument.Parse(datos).RootElement;
            return new Cuenta(o.GetProperty("usuario").GetString() ?? "", o.GetProperty("clave").GetString() ?? "");
        }
        catch (JsonException) { return null; }
    }

    public static void Olvidar(string sitio)
    {
        if (OperatingSystem.IsMacOS()) Mac.Olvidar(sitio);
        else if (OperatingSystem.IsLinux()) Linux.Olvidar(sitio);
    }

    // libsecret, con las funciones que reciben los atributos en una tabla (no variádicas).
    private static class Linux
    {
        private const string Secret = "libsecret-1.so.0";
        private const string GLib = "libglib-2.0.so.0";

        public static bool Disponible
        {
            get
            {
                try { return NativeLibrary.TryLoad(Secret, out _); }
                catch { return false; }
            }
        }

        public static void Guardar(string sitio, string datos)
            => ConAtributos(sitio, a =>
            {
                if (!secret_password_storev_sync(IntPtr.Zero, a, null, $"Universidad Kennedy MiPortal App ({sitio})", datos, IntPtr.Zero, out var e))
                    throw Error(e, "no se pudo guardar en el llavero");
            });

        public static string? Leer(string sitio)
            => ConAtributos(sitio, a =>
            {
                var p = secret_password_lookupv_sync(IntPtr.Zero, a, IntPtr.Zero, out var e);
                if (e != IntPtr.Zero) throw Error(e, "no se pudo leer el llavero");
                if (p == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUTF8(p); }
                finally { secret_password_free(p); }
            });

        public static void Olvidar(string sitio)
            => ConAtributos(sitio, a => { secret_password_clearv_sync(IntPtr.Zero, a, IntPtr.Zero, out var e); if (e != IntPtr.Zero) g_error_free(e); });

        private static void ConAtributos(string sitio, Action<IntPtr> accion) => ConAtributos<object?>(sitio, a => { accion(a); return null; });

        // Atributos con los que se encuentra el secreto: la app y el sitio.
        private static T ConAtributos<T>(string sitio, Func<IntPtr, T> accion)
        {
            var glib = NativeLibrary.Load(GLib);
            var tabla = g_hash_table_new(NativeLibrary.GetExport(glib, "g_str_hash"), NativeLibrary.GetExport(glib, "g_str_equal"));
            var cadenas = new List<IntPtr>();
            IntPtr C(string s) { var p = Marshal.StringToCoTaskMemUTF8(s); cadenas.Add(p); return p; }
            try
            {
                g_hash_table_insert(tabla, C("aplicacion"), C(Aplicacion));
                g_hash_table_insert(tabla, C("sitio"), C(sitio));
                return accion(tabla);
            }
            finally
            {
                g_hash_table_unref(tabla);
                cadenas.ForEach(Marshal.FreeCoTaskMem);
            }
        }

        private static InvalidOperationException Error(IntPtr error, string que)
        {
            // GError: dominio (uint), código (int), mensaje (char*).
            var mensaje = error == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8));
            if (error != IntPtr.Zero) g_error_free(error);
            return new InvalidOperationException($"{que}: {mensaje}");
        }

        [DllImport(Secret)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool secret_password_storev_sync(IntPtr esquema, IntPtr atributos, string? coleccion, string etiqueta, string clave, IntPtr cancelable, out IntPtr error);
        [DllImport(Secret)] private static extern IntPtr secret_password_lookupv_sync(IntPtr esquema, IntPtr atributos, IntPtr cancelable, out IntPtr error);
        [DllImport(Secret)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool secret_password_clearv_sync(IntPtr esquema, IntPtr atributos, IntPtr cancelable, out IntPtr error);
        [DllImport(Secret)] private static extern void secret_password_free(IntPtr clave);
        [DllImport(GLib)] private static extern IntPtr g_hash_table_new(IntPtr hash, IntPtr igual);
        [DllImport(GLib)] private static extern void g_hash_table_insert(IntPtr tabla, IntPtr clave, IntPtr valor);
        [DllImport(GLib)] private static extern void g_hash_table_unref(IntPtr tabla);
        [DllImport(GLib)] private static extern void g_error_free(IntPtr error);
    }

    // Llavero de macOS: contraseña genérica (servicio = la app, cuenta = el sitio).
    private static class Mac
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const int NoEncontrado = -25300;

        public static void Guardar(string sitio, string datos)
        {
            var bytes = Encoding.UTF8.GetBytes(datos);
            var (s, c) = (Encoding.UTF8.GetBytes(Aplicacion), Encoding.UTF8.GetBytes(sitio));
            if (SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)c.Length, c, out _, out var viejos, out var item) == 0)
            {
                SecKeychainItemFreeContent(IntPtr.Zero, viejos);
                var r = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)bytes.Length, bytes);
                CFRelease(item);
                if (r != 0) throw new InvalidOperationException($"no se pudo actualizar el llavero ({r})");
                return;
            }
            var res = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)c.Length, c, (uint)bytes.Length, bytes, IntPtr.Zero);
            if (res != 0) throw new InvalidOperationException($"no se pudo guardar en el llavero ({res})");
        }

        public static string? Leer(string sitio)
        {
            var (s, c) = (Encoding.UTF8.GetBytes(Aplicacion), Encoding.UTF8.GetBytes(sitio));
            var r = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)c.Length, c, out var largo, out var datos, out var item);
            if (r == NoEncontrado) return null;
            if (r != 0) throw new InvalidOperationException($"no se pudo leer el llavero ({r})");
            try { return Marshal.PtrToStringUTF8(datos, (int)largo); }
            finally { SecKeychainItemFreeContent(IntPtr.Zero, datos); CFRelease(item); }
        }

        public static void Olvidar(string sitio)
        {
            var (s, c) = (Encoding.UTF8.GetBytes(Aplicacion), Encoding.UTF8.GetBytes(sitio));
            if (SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)c.Length, c, out _, out var datos, out var item) != 0) return;
            SecKeychainItemFreeContent(IntPtr.Zero, datos);
            SecKeychainItemDelete(item);
            CFRelease(item);
        }

        [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(IntPtr llavero, uint largoServicio, byte[] servicio,
            uint largoCuenta, byte[] cuenta, uint largoClave, byte[] clave, IntPtr item);
        [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(IntPtr llaveros, uint largoServicio, byte[] servicio,
            uint largoCuenta, byte[] cuenta, out uint largoClave, out IntPtr clave, out IntPtr item);
        [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr atributos, uint largo, byte[] datos);
        [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr atributos, IntPtr datos);
        [DllImport(Security)] private static extern int SecKeychainItemDelete(IntPtr item);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr objeto);
    }
}
