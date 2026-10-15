// SPDX-License-Identifier: MIT
using System.Collections;
using System.Windows;

namespace Plata.Compat;

/// <summary>
/// <c>VisualStateManager.VisualStateGroups</c> para el XAML en tiempo de ejecución. El lector de
/// OpenSilver lee las propiedades adjuntas con <c>GetValue(dp)</c> y la de los grupos vale null
/// hasta que alguien llama a <c>GetVisualStateGroups</c>, que es quien la crea; entonces falla
/// ("The value for 'VisualStateGroups' property is null"). <see cref="CargadorDeXaml"/> reescribe
/// el XAML para usar esta propiedad, que a propósito no tiene DependencyProperty: así el lector usa
/// el getter, que devuelve la colección real.
/// </summary>
public static class Estados
{
    public static IList GetGrupos(FrameworkElement elemento) => VisualStateManager.GetVisualStateGroups(elemento);
}
