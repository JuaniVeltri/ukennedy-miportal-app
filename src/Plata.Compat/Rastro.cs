// SPDX-License-Identifier: MIT
namespace Plata.Compat;

/// <summary>
/// Rastreo para desarrollo: el traductor inserta una llamada a <see cref="Entrar"/> al comienzo
/// de los métodos de la app que se pidan (ver ReferenceRetargeter.Rastrear).
/// </summary>
public static class Rastro
{
    public static void Entrar(string metodo) => Avisos.Emitir("rastro> " + metodo);
}
