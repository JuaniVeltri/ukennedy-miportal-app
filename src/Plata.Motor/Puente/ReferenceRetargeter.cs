// SPDX-License-Identifier: MIT
using Mono.Cecil;

namespace Plata.Motor.Puente;

/// <summary>
/// Reescribe, en memoria, las referencias de ensamblado de una librería compilada
/// para Silverlight de modo que apunten a OpenSilver. Las referencias que no son de
/// Silverlight quedan intactas.
/// </summary>
public sealed class ReferenceRetargeter
{
    private static readonly Version OpenSilverVersion = new(3, 3, 3, 0);

    public byte[] Traducir(byte[] ensamblado, out IReadOnlyList<string> referenciasReescritas)
    {
        var reescritas = new List<string>();
        using var mod = ModuleDefinition.ReadModule(new MemoryStream(ensamblado));

        var aRedirigir = mod.AssemblyReferences
            .Where(a => MapaDeEnsamblados.Destino(a.Name) == "OpenSilver")
            .ToList();

        if (aRedirigir.Count == 0)
        {
            referenciasReescritas = reescritas;
            return ensamblado;
        }

        // Una sola referencia a OpenSilver: reusar si ya existe, si no renombrar la primera.
        var osRef = mod.AssemblyReferences.FirstOrDefault(a => a.Name == "OpenSilver");
        if (osRef is null)
        {
            osRef = aRedirigir[0];
            reescritas.Add(osRef.Name);
            osRef.Name = "OpenSilver";
            osRef.PublicKeyToken = Array.Empty<byte>();
            osRef.Version = OpenSilverVersion;
            aRedirigir.RemoveAt(0);
        }

        // El resto de referencias redirigibles se eliminan y sus tipos se reapuntan a osRef.
        foreach (var r in aRedirigir)
        {
            reescritas.Add(r.Name);
            foreach (var t in mod.GetTypeReferences())
                if (ReferenceEquals(t.Scope, r)) t.Scope = osRef;
            mod.AssemblyReferences.Remove(r);
        }

        using var salida = new MemoryStream();
        mod.Write(salida);
        referenciasReescritas = reescritas;
        return salida.ToArray();
    }
}
