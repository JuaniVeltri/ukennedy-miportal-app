// SPDX-License-Identifier: MIT
namespace Plata.Motor.Puente;

/// <summary>
/// Decide, por nombre de ensamblado, cuáles referencias de Silverlight se redirigen
/// a OpenSilver. El resto de la BCL (mscorlib, System, etc.) la resuelve el runtime
/// de .NET moderno por nombre al no encontrar la versión 2.0.5.0 de Silverlight.
/// </summary>
public static class MapaDeEnsamblados
{
    private static readonly HashSet<string> HaciaOpenSilver = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Windows", "System.Windows.Browser", "System.Windows.Controls",
        "System.Windows.Controls.Data", "System.Windows.Controls.Data.Input",
        "System.Windows.Controls.Navigation", "System.Windows.Controls.Toolkit",
        "System.ServiceModel", "System.ServiceModel.Web",
    };

    public static string? Destino(string nombre)
        => HaciaOpenSilver.Contains(nombre) ? "OpenSilver" : null;
}
