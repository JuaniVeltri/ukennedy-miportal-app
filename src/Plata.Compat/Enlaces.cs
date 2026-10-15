// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Windows;
using System.Windows.Data;

namespace Plata.Compat;

/// <summary>
/// Bindings <c>OneTime</c> con la semántica de Silverlight: ahí se evalúan cuando el elemento
/// entra al árbol; OpenSilver los evalúa al leer el XAML y no los repite. Si el valor depende de
/// algo que llega después (p. ej. las traducciones de GGI), queda vacío. <see cref="CargadorDeXaml"/>
/// marca esos elementos con <c>plata:Enlaces.Refrescar="Propiedad,…"</c> y acá, al cargarse el
/// elemento, se vuelven a aplicar esos bindings.
/// </summary>
public static class Enlaces
{
    public static readonly DependencyProperty RefrescarProperty = DependencyProperty.RegisterAttached(
        "Refrescar", typeof(string), typeof(Enlaces), new PropertyMetadata(null, AlAsignar));

    public static string GetRefrescar(DependencyObject d) => (string)d.GetValue(RefrescarProperty);
    public static void SetRefrescar(DependencyObject d, string valor) => d.SetValue(RefrescarProperty, valor);

    private static void AlAsignar(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement elemento || e.NewValue is not string propiedades) return;
        RoutedEventHandler? alCargar = null;
        alCargar = (_, _) =>
        {
            elemento.Loaded -= alCargar;
            foreach (var nombre in propiedades.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var dp = Propiedad(elemento.GetType(), nombre);
                if (dp is null || elemento.GetBindingExpression(dp) is not { } expresion) continue;
                elemento.SetBinding(dp, expresion.ParentBinding);
            }
        };
        elemento.Loaded += alCargar;
    }

    // La DependencyProperty "<Nombre>Property" del tipo o sus bases.
    private static DependencyProperty? Propiedad(Type tipo, string nombre)
        => tipo.GetField(nombre + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            ?.GetValue(null) as DependencyProperty;
}
