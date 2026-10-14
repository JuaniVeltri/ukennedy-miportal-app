// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Xml.Linq;

namespace Plata.Motor.Xap;

/// <summary>Un .xap abierto: su manifiesto y el contenido de sus entradas, en memoria.</summary>
public sealed class PaqueteXap
{
    public required AppManifest Manifest { get; init; }
    public required IReadOnlyDictionary<string, byte[]> Entradas { get; init; }

    /// <summary>Devuelve los bytes de una librería por su nombre sin extensión.</summary>
    public byte[] Assembly(string nombreSinExtension)
    {
        var part = Manifest.Parts.FirstOrDefault(p =>
            Path.GetFileNameWithoutExtension(p.Source)
                .Equals(nombreSinExtension, StringComparison.OrdinalIgnoreCase));
        var clave = part?.Source ?? nombreSinExtension + ".dll";
        if (!Entradas.TryGetValue(clave, out var bytes))
            throw new PlataXapInvalidoException($"El .xap no contiene la librería '{clave}'.");
        return bytes;
    }
}

public static class XapReader
{
    private static readonly XNamespace D = "http://schemas.microsoft.com/client/2007/deployment";

    public static PaqueteXap Leer(Stream xap)
    {
        Dictionary<string, byte[]> entradas = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var zip = new ZipArchive(xap, ZipArchiveMode.Read);
            foreach (var e in zip.Entries)
            {
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                entradas[e.FullName.Replace('\\', '/')] = ms.ToArray();
            }
        }
        catch (InvalidDataException ex)
        {
            throw new PlataXapInvalidoException("El archivo no es un .xap válido (no es un ZIP).", ex);
        }

        if (!entradas.TryGetValue("AppManifest.xaml", out var manBytes))
            throw new PlataXapInvalidoException("El .xap no tiene AppManifest.xaml.");

        return new PaqueteXap { Manifest = LeerManifiesto(manBytes), Entradas = entradas };
    }

    private static AppManifest LeerManifiesto(byte[] bytes)
    {
        XDocument doc;
        try { doc = XDocument.Load(new MemoryStream(bytes)); }
        catch (Exception ex) { throw new PlataXapInvalidoException("AppManifest.xaml no es XML válido.", ex); }

        var root = doc.Root ?? throw new PlataXapInvalidoException("AppManifest.xaml está vacío.");
        string Req(string attr) => (string?)root.Attribute(attr)
            ?? throw new PlataXapInvalidoException($"AppManifest.xaml no tiene el atributo '{attr}'.");

        var parts = root.Element(D + "Deployment.Parts")?.Elements(D + "AssemblyPart")
            .Select(p => new AssemblyPart((string?)p.Attribute("Source") ?? ""))
            .Where(p => p.Source.Length > 0).ToList() ?? new List<AssemblyPart>();

        return new AppManifest(Req("EntryPointAssembly"), Req("EntryPointType"),
            (string?)root.Attribute("RuntimeVersion") ?? "", parts);
    }
}
