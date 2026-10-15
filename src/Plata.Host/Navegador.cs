// SPDX-License-Identifier: MIT
namespace Plata.Host;

/// <summary>
/// Prepara el navegador de la ventana del portal para correr el portal enmarcado: scripts del
/// portal, cookies de terceros y el esquema del lanzador. Cada sistema lo resuelve con su motor
/// (WebKitGTK en Linux, WKWebView en macOS); en otros no hace nada.
/// </summary>
public static class Navegador
{
    /// <param name="titulo">Título de la ventana (para encontrarla en Linux).</param>
    public static void Preparar(string titulo, string scripts, string esquemaDelLanzador)
    {
        if (OperatingSystem.IsLinux())
        {
            WebKitGtk.MarcarEsquemaComoSeguro(esquemaDelLanzador);
            WebKitGtk.AceptarCookiesDeTerceros();
            WebKitGtk.AgregarScript(titulo, scripts);
        }
        else if (OperatingSystem.IsMacOS())
        {
            WebKitMac.PermitirCookiesDelPortal();
            WebKitMac.AceptarCookiesDeTerceros();
            WebKitMac.AgregarScript(scripts);
        }
    }
}
