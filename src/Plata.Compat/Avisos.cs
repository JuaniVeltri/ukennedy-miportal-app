// SPDX-License-Identifier: MIT
namespace Plata.Compat;

/// <summary>
/// Canal de avisos de compatibilidad (lo que la app pidió y Plata no tiene). El motor se
/// suscribe y los reenvía al host, que los junta para el informe de compatibilidad.
/// </summary>
public static class Avisos
{
    public static event Action<string>? Emitido;

    internal static void Emitir(string aviso) => Emitido?.Invoke(aviso);
}
