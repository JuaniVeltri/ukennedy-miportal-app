// SPDX-License-Identifier: MIT
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Plata.Motor.Puente;

/// <summary>
/// Reescribe, en memoria, una librería compilada para Silverlight para que corra
/// sobre OpenSilver y la BCL moderna:
///  - referencias a ensamblados de UI de Silverlight → OpenSilver;
///  - referencias de tipo de la BCL de Silverlight (System, System.Core, …) →
///    el ensamblado moderno de .NET que realmente contiene cada tipo.
/// </summary>
public sealed class ReferenceRetargeter
{
    private static readonly Version OpenSilverVersion = new(3, 3, 3, 0);

    // BCL de Silverlight cuyos tipos hay que reubicar en .NET moderno (sin mscorlib:
    // su fachada moderna ya reenvía los tipos del núcleo por nombre).
    private static readonly HashSet<string> BclAUnificar = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "System.Core", "System.Xml", "System.Net",
        "System.Runtime.Serialization", "System.ComponentModel.DataAnnotations",
        // UI de Silverlight: cada tipo se reubica en el ensamblado OpenSilver real
        // que lo exporta (p. ej. HtmlPage vive en OpenSilver.Browser, no en OpenSilver).
        "System.Windows", "System.Windows.Browser", "System.Windows.Controls",
        "System.Windows.Controls.Data", "System.Windows.Controls.Data.Input",
        "System.Windows.Controls.Navigation", "System.Windows.Controls.Toolkit",
        "System.Windows.Controls.Layout.Toolkit", "System.Windows.Controls.Input.Toolkit",
        "System.Windows.Controls.DataVisualization.Toolkit", "System.Windows.Data",
    };

    // Caché global: nombre completo de tipo → ensamblado moderno que lo exporta.
    private static readonly Dictionary<string, string?> _modernaDeTipo = new(StringComparer.Ordinal);

    public byte[] Traducir(byte[] ensamblado, out IReadOnlyList<string> referenciasReescritas)
    {
        var reescritas = new List<string>();
        using var mod = ModuleDefinition.ReadModule(new MemoryStream(ensamblado));

        var cambiado = UnificarBcl(mod);
        cambiado |= RedirigirUiAOpenSilver(mod, reescritas);
        cambiado |= SaltearApiAusente(mod);

        referenciasReescritas = reescritas;
        if (!cambiado) return ensamblado;

        using var salida = new MemoryStream();
        mod.Write(salida);
        return salida.ToArray();
    }

    // Reubica cada referencia de tipo de la BCL de Silverlight al ensamblado moderno.
    private static bool UnificarBcl(ModuleDefinition mod)
    {
        var refsModernas = new Dictionary<string, AssemblyNameReference>(StringComparer.Ordinal);
        var cambiado = false;

        foreach (var t in mod.GetTypeReferences())
        {
            if (t.Scope is not AssemblyNameReference an || !BclAUnificar.Contains(an.Name)) continue;
            var moderna = ModernaDeTipo(t.FullName.Replace('/', '+'));
            if (moderna is null || moderna == an.Name) continue;

            if (!refsModernas.TryGetValue(moderna, out var nueva))
            {
                nueva = mod.AssemblyReferences.FirstOrDefault(a => a.Name == moderna)
                        ?? new AssemblyNameReference(moderna, new Version(0, 0, 0, 0));
                if (!mod.AssemblyReferences.Contains(nueva)) mod.AssemblyReferences.Add(nueva);
                refsModernas[moderna] = nueva;
            }
            t.Scope = nueva;
            cambiado = true;
        }
        return cambiado;
    }

    // Métodos de OpenSilver con firma distinta a Silverlight que la app llama y
    // cuyo efecto es prescindible en un visor: se anula la llamada (pop de args).
    private static readonly HashSet<string> MetodosASaltear = new(StringComparer.Ordinal)
    {
        "System.Windows.Application::add_Exit",
        "System.Windows.Application::remove_Exit",
    };

    private static bool SaltearApiAusente(ModuleDefinition mod)
    {
        var cambiado = false;
        foreach (var tipo in mod.GetTypes())
            foreach (var m in tipo.Methods)
            {
                if (!m.HasBody) continue;
                var il = m.Body.GetILProcessor();
                foreach (var instr in m.Body.Instructions.ToList())
                {
                    if (instr.Operand is not MethodReference mr) continue;
                    if (!MetodosASaltear.Contains(mr.DeclaringType.FullName + "::" + mr.Name)) continue;
                    // add_Exit(this, delegate) -> void: descartar los 2 valores de la pila.
                    var pop1 = Instruction.Create(OpCodes.Pop);
                    il.Replace(instr, pop1);
                    il.InsertAfter(pop1, Instruction.Create(OpCodes.Pop));
                    cambiado = true;
                }
            }
        return cambiado;
    }

    private static string? ModernaDeTipo(string fullName)
    {
        if (_modernaDeTipo.TryGetValue(fullName, out var cache)) return cache;
        Type? tipo = Type.GetType(fullName);
        if (tipo is null)
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { tipo = asm.GetType(fullName); } catch { tipo = null; }
                if (tipo is not null) break;
            }
        var nombre = tipo?.Assembly.GetName().Name;
        _modernaDeTipo[fullName] = nombre;
        return nombre;
    }

    // System.Windows* (UI de Silverlight) → OpenSilver.
    private static bool RedirigirUiAOpenSilver(ModuleDefinition mod, List<string> reescritas)
    {
        var aRedirigir = mod.AssemblyReferences
            .Where(a => MapaDeEnsamblados.Destino(a.Name) == "OpenSilver")
            .ToList();
        if (aRedirigir.Count == 0) return false;

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

        foreach (var r in aRedirigir)
        {
            reescritas.Add(r.Name);
            foreach (var t in mod.GetTypeReferences())
                if (ReferenceEquals(t.Scope, r)) t.Scope = osRef;
            mod.AssemblyReferences.Remove(r);
        }
        return true;
    }
}
