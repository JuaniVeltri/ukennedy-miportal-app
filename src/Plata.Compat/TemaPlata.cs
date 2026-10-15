// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Windows;

namespace Plata.Compat;

/// <summary>
/// Estilos por defecto de los controles de la app y de sus librerías (p. ej. SilverlightContrib):
/// el <c>themes/generic.xaml</c> de cada ensamblado. OpenSilver solo encuentra los que compila su
/// propio compilador; para los ensamblados de OpenSilver se devuelve null y usa los suyos.
/// </summary>
public sealed class TemaPlata : OpenSilver.Theming.Theme
{
    private readonly HashSet<string> _respaldados = new(StringComparer.OrdinalIgnoreCase);

    protected override ResourceDictionary? GenerateResources(Assembly assembly)
        => CargadorDeXaml.CargarDiccionario(assembly, "themes/generic.xaml");

    /// <summary>
    /// Los templates se procesan recién al aplicarse, y el lector de XAML de OpenSilver ya no
    /// ve entonces el diccionario donde se definieron: un StaticResource a una clave del mismo
    /// diccionario no se encuentra. Como último recurso, StaticResource busca en el tema; acá se
    /// agrega (una vez por archivo) cada diccionario cargado desde un Source.
    /// </summary>
    internal static void Respaldar(string archivo, ResourceDictionary diccionario)
    {
        if (Application.Current?.Theme is not TemaPlata tema || !tema._respaldados.Add(archivo)) return;
        tema.Resources.MergedDictionaries.Add(diccionario);
    }
}
