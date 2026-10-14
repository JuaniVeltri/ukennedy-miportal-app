// SPDX-License-Identifier: MIT
using Plata.Motor.Xap;

namespace Plata.Motor.Puente;

/// <summary>
/// A partir del manifiesto, decide qué librerías del .xap hay que cargar (las de
/// la app y terceros) y cuáles se saltean porque OpenSilver ya las provee o son
/// recursos de idioma.
/// </summary>
public static class PlanDeCarga
{
    public static IReadOnlyList<AssemblyPart> AcCargar(AppManifest manifest)
    {
        return manifest.Parts.Where(p =>
        {
            var nombre = Path.GetFileNameWithoutExtension(p.Source);
            if (p.Source.Contains('/') || p.Source.Contains('\\')) return false; // recursos es/*.resources.dll
            if (nombre.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return false;
            if (MapaDeEnsamblados.EsProvistoPorOpenSilver(nombre)) return false;
            return true;
        }).ToList();
    }
}
