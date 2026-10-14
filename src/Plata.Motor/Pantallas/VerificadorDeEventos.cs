// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Xml.Linq;
using Plata.Motor.Informe;

namespace Plata.Motor.Pantallas;

/// <summary>
/// Revisa el XAML de una pantalla: cada atributo de evento (p. ej. Click="Boton_Click")
/// debe corresponder a un método del tipo de la pantalla. Los que no existen se anotan
/// en el informe, en lugar de fallar en silencio cuando OpenSilver ignora el método ausente.
/// </summary>
public static class VerificadorDeEventos
{
    // Nombres de evento de Silverlight usados por las aplicaciones de formularios.
    private static readonly HashSet<string> Eventos = new(StringComparer.Ordinal)
    {
        "Click", "Loaded", "Unloaded", "SelectionChanged", "TextChanged",
        "Checked", "Unchecked", "MouseLeftButtonDown", "MouseLeftButtonUp",
        "KeyDown", "KeyUp", "GotFocus", "LostFocus", "LayoutUpdated",
    };

    public static void Verificar(Type tipoPantalla, string xaml, InformeDeCompatibilidad informe)
    {
        var metodos = tipoPantalla
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var el in XDocument.Parse(xaml).Descendants())
            foreach (var attr in el.Attributes())
            {
                if (attr.IsNamespaceDeclaration) continue;
                if (!Eventos.Contains(attr.Name.LocalName)) continue;

                var metodo = attr.Value;
                // Un evento referencia un método por nombre, no un binding ni una ruta.
                if (metodo.Contains('{') || metodo.Contains('/') || metodo.Contains(' ')) continue;
                if (!metodos.Contains(metodo))
                    informe.Anotar("evento",
                        $"{el.Name.LocalName}.{attr.Name.LocalName}=\"{metodo}\" (método {metodo} ausente)");
            }
    }
}
