// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Plata.Compat;

/// <summary>
/// <c>Loaded</c> con la semántica de Silverlight: ahí llega con el layout ya hecho y las apps
/// leen tamaños (<c>ActualHeight</c> del padre, etc.). OpenSilver lo dispara al insertar el
/// elemento, antes de medir. Acá cada handler se ejecuta después (Dispatcher), con el layout al día.
/// </summary>
public static class Diferido
{
    /// <summary>
    /// <c>Loaded="Handler"</c> del XAML, reescrito por <see cref="CargadorDeXaml"/>: el handler
    /// es un método de la raíz que se está cargando.
    /// </summary>
    public static readonly DependencyProperty CargadoProperty = DependencyProperty.RegisterAttached(
        "Cargado", typeof(string), typeof(Diferido), new PropertyMetadata(null, AlAsignarCargado));

    public static string GetCargado(DependencyObject d) => (string)d.GetValue(CargadoProperty);
    public static void SetCargado(DependencyObject d, string valor) => d.SetValue(CargadoProperty, valor);

    // La raíz del XAML que se está cargando (la fija CargadorDeXaml).
    [ThreadStatic] internal static object? RaizActual;

    private static readonly ConditionalWeakTable<FrameworkElement, List<(RoutedEventHandler Original, RoutedEventHandler Envuelto)>>
        Suscripciones = new();

    private static void AlAsignarCargado(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement elemento || e.NewValue is not string nombre || RaizActual is not { } raiz) return;
        var metodo = raiz.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == nombre && m.GetParameters().Length == 2);
        if (metodo is null)
        {
            Avisos.Emitir($"handler de Loaded no encontrado: {raiz.GetType().Name}.{nombre}");
            return;
        }
        Suscribir(elemento, (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), raiz, metodo));
    }

    /// <summary><c>elemento.Loaded += handler</c> en el código de la app.</summary>
    public static void Suscribir(FrameworkElement elemento, RoutedEventHandler handler)
    {
        RoutedEventHandler envuelto = (s, a) => elemento.Dispatcher.BeginInvoke(() =>
        {
            elemento.UpdateLayout();
            handler(s, a);
        });
        Suscripciones.GetOrCreateValue(elemento).Add((handler, envuelto));
        elemento.Loaded += envuelto;
    }

    /// <summary><c>elemento.Loaded -= handler</c> en el código de la app.</summary>
    public static void Desuscribir(FrameworkElement elemento, RoutedEventHandler handler)
    {
        if (!Suscripciones.TryGetValue(elemento, out var lista)) return;
        var i = lista.FindIndex(x => x.Original.Equals(handler));
        if (i < 0) return;
        elemento.Loaded -= lista[i].Envuelto;
        lista.RemoveAt(i);
    }
}
