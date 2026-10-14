// SPDX-License-Identifier: MIT
namespace Plata.Motor.Puente;

/// <summary>
/// Decide, por nombre de ensamblado, cuáles de Silverlight provee OpenSilver.
/// Esas no se cargan del .xap: se usan las de OpenSilver, y las referencias a
/// ellas se redirigen. El resto de la BCL la resuelve .NET moderno por nombre.
/// </summary>
public static class MapaDeEnsamblados
{
    // Núcleo y SDK/Toolkit de Silverlight que OpenSilver provee con el mismo API.
    private static readonly HashSet<string> ProvistoPorOpenSilver = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Windows", "System.Windows.Browser",
        "System.Windows.Controls", "System.Windows.Controls.Data",
        "System.Windows.Controls.Data.Input", "System.Windows.Controls.Navigation",
        "System.Windows.Controls.Toolkit", "System.Windows.Controls.Layout.Toolkit",
        "System.Windows.Controls.Input.Toolkit", "System.Windows.Controls.DataVisualization.Toolkit",
        "System.Windows.Data", "System.ServiceModel", "System.ServiceModel.Web",
    };

    /// <summary>OpenSilver provee este ensamblado; no cargar la copia del .xap.</summary>
    public static bool EsProvistoPorOpenSilver(string nombre) => ProvistoPorOpenSilver.Contains(nombre);

    public static string? Destino(string nombre)
        => ProvistoPorOpenSilver.Contains(nombre) ? "OpenSilver" : null;
}
