// SPDX-License-Identifier: MIT
namespace Plata.Compat;

/// <summary>
/// Navegación de la app a otras páginas (<c>HtmlPage.Window.Navigate</c>, <c>HtmlPage.PopupWindow</c>):
/// reportes, documentos, páginas de error. En Silverlight navegaba la página que contenía la app;
/// en Plata eso la reemplazaría, así que el host abre la dirección aparte (diseño, §2 "Ventanas").
/// Las direcciones relativas se resuelven contra la página de origen, como en Silverlight.
/// </summary>
public static class Ventanas
{
    /// <summary>Lo conecta el motor: le pide al host que abra la dirección.</summary>
    public static Action<Uri>? Abrir { get; set; }

    internal static void Pedir(Uri direccion)
    {
        var absoluta = direccion.IsAbsoluteUri ? direccion
            : new Uri(Origen.Pagina ?? Origen.Xap
                      ?? throw new InvalidOperationException($"Plata: dirección relativa '{direccion}' sin página de origen."), direccion);
        if (Abrir is { } abrir) abrir(absoluta);
        else Avisos.Emitir("no hay quien abra ventanas: " + absoluta.GetLeftPart(UriPartial.Path));
    }
}
