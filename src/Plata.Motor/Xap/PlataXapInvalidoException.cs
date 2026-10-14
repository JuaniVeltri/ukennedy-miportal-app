// SPDX-License-Identifier: MIT
namespace Plata.Motor.Xap;

/// <summary>Un .xap no se pudo abrir o su manifiesto es inválido o incompleto.</summary>
public sealed class PlataXapInvalidoException : Exception
{
    public PlataXapInvalidoException(string mensaje) : base(mensaje) { }
    public PlataXapInvalidoException(string mensaje, Exception inner) : base(mensaje, inner) { }
}
