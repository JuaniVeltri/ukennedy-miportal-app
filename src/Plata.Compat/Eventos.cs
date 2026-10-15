// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Windows;

namespace Plata.Compat;

/// <summary>
/// Eventos declarados dentro de templates (<c>LoadingRow="Grilla_LoadingRow"</c> en un DataTemplate).
/// En Silverlight se conectan al code-behind de la pantalla; OpenSilver, al instanciar el template,
/// busca el método en la raíz del template y falla. <see cref="CargadorDeXaml"/> reescribe esos
/// atributos como <c>plata:Eventos.Conectar="id|Evento:Handler;…"</c>, donde id identifica la
/// pantalla, y acá se conectan por reflexión.
/// </summary>
public static class Eventos
{
    public static readonly DependencyProperty ConectarProperty = DependencyProperty.RegisterAttached(
        "Conectar", typeof(string), typeof(Eventos), new PropertyMetadata(null, AlAsignar));

    public static string GetConectar(DependencyObject d) => (string)d.GetValue(ConectarProperty);
    public static void SetConectar(DependencyObject d, string valor) => d.SetValue(ConectarProperty, valor);

    private static readonly Dictionary<int, WeakReference<object>> Raices = new();
    private static int _ultima;

    /// <summary>Registra la raíz de un documento y devuelve su id para los atributos reescritos.</summary>
    internal static int Registrar(object raiz)
    {
        var id = Interlocked.Increment(ref _ultima);
        lock (Raices) Raices[id] = new WeakReference<object>(raiz);
        return id;
    }

    private static void AlAsignar(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string valor || valor.Split('|', 2) is not [var id, var pares]) return;
        object? raiz = null;
        lock (Raices)
            if (!int.TryParse(id, out var n) || !Raices.TryGetValue(n, out var r) || !r.TryGetTarget(out raiz)) return;

        foreach (var par in pares.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var (evento, handler) = par.Split(':', 2) is [var a, var b] ? (a, b) : ("", "");
            var info = d.GetType().GetEvent(evento);
            var metodo = Metodo(raiz.GetType(), handler);
            if (info?.EventHandlerType is null || metodo is null)
            {
                Avisos.Emitir($"evento de template sin conectar: {d.GetType().Name}.{evento} → {raiz.GetType().Name}.{handler}");
                continue;
            }
            var delegado = Delegate.CreateDelegate(info.EventHandlerType, raiz, metodo, throwOnBindFailure: false);
            if (delegado is null) { Avisos.Emitir($"firma incompatible: {raiz.GetType().Name}.{handler} para {evento}"); continue; }
            if (evento == "Loaded" && d is FrameworkElement fe && delegado is RoutedEventHandler cargado)
                Diferido.Suscribir(fe, cargado);
            else
                info.AddEventHandler(d, delegado);
        }
    }

    // Métodos (de cualquier visibilidad) del tipo y sus bases.
    internal static MethodInfo? Metodo(Type? tipo, string nombre)
    {
        for (; tipo is not null; tipo = tipo.BaseType)
        {
            var m = tipo.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(x => x.Name == nombre && x.GetParameters() is [_, var p] && typeof(EventArgs).IsAssignableFrom(p.ParameterType));
            if (m is not null) return m;
        }
        return null;
    }
}
