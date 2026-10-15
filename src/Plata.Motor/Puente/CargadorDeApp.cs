// SPDX-License-Identifier: MIT
using System.Reflection;
using Plata.Motor.Xap;

namespace Plata.Motor.Puente;

/// <summary>
/// Carga las librerías de una app Silverlight (según el plan de carga), reescribe
/// sus referencias a OpenSilver y las hace resolubles por nombre entre sí. Devuelve
/// el tipo de entrada (la clase Application de la app), lista para instanciar.
/// </summary>
public sealed class CargadorDeApp
{
    private readonly Dictionary<string, Assembly> _cargadas = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _resolviendo = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReferenceRetargeter _retargeter = new();
    private bool _resolverInstalado;

    public Type CargarTipoDeEntrada(PaqueteXap pkg)
    {
        InstalarResolver();
        foreach (var part in PlanDeCarga.AcCargar(pkg.Manifest))
        {
            var traducida = _retargeter.Traducir(pkg.Entradas[part.Source], out _);
            var asm = Assembly.Load(traducida);
            _cargadas[asm.GetName().Name!] = asm;
        }

        if (!_cargadas.TryGetValue(pkg.Manifest.EntryPointAssembly, out var entry))
            throw new PlataXapInvalidoException(
                $"No se cargó el ensamblado de entrada '{pkg.Manifest.EntryPointAssembly}'.");

        return entry.GetType(pkg.Manifest.EntryPointType, throwOnError: true)!;
    }

    /// <summary>
    /// Carga una librería que la app trae en tiempo de ejecución (<c>AssemblyPart.Load</c>):
    /// la traduce como las del .xap. Si ya está cargada devuelve esa (no se duplican tipos);
    /// las del SDK que provee OpenSilver y los recursos de idioma no se cargan (null).
    /// </summary>
    public Assembly? CargarEnsamblado(byte[] bytes)
    {
        InstalarResolver();
        string nombre;
        using (var mod = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(bytes)))
            nombre = mod.Assembly.Name.Name;
        if (_cargadas.TryGetValue(nombre, out var ya)) return ya;
        if (MapaDeEnsamblados.EsProvistoPorOpenSilver(nombre) ||
            nombre.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
            return null;

        var asm = Assembly.Load(_retargeter.Traducir(bytes, out _));
        _cargadas[nombre] = asm;
        return asm;
    }

    // Las librerías cargadas por bytes no se encuentran por nombre: este resolvedor
    // se las sirve al runtime cuando una referencia a otra se necesita.
    private void InstalarResolver()
    {
        if (_resolverInstalado) return;
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var nombre = new AssemblyName(e.Name).Name!;
            if (_cargadas.TryGetValue(nombre, out var a)) return a;
            // Evita recursión infinita: si ya estamos resolviendo este nombre, cortar.
            if (!_resolviendo.Add(nombre)) return null;
            try
            {
                // BCL de Silverlight (System.Core, System.Xml, etc.) pedida por su
                // identidad 2.0.5.0: resolverla a la equivalente moderna por nombre.
                return Assembly.Load(new AssemblyName(nombre));
            }
            catch { return null; }
            finally { _resolviendo.Remove(nombre); }
        };
        _resolverInstalado = true;
    }
}
