// SPDX-License-Identifier: MIT
namespace Plata.Motor;

/// <summary>Una aplicación usó una capacidad de Silverlight que Plata todavía no soporta.</summary>
public sealed class PlataNoSoportadoException(string queFalta)
    : Exception($"Plata todavía no soporta: {queFalta}")
{
    public string QueFalta { get; } = queFalta;
}
