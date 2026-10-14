// SPDX-License-Identifier: MIT
namespace Plata.Motor.Xap;

public record AssemblyPart(string Source);

/// <summary>Datos del AppManifest.xaml de un .xap de Silverlight.</summary>
public record AppManifest(
    string EntryPointAssembly,
    string EntryPointType,
    string RuntimeVersion,
    IReadOnlyList<AssemblyPart> Parts);
