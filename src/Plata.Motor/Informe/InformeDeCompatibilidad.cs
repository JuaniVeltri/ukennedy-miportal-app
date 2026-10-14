// SPDX-License-Identifier: MIT
using System.Text.Json;

namespace Plata.Motor.Informe;

/// <summary>
/// Junta lo que una aplicación necesita y Plata todavía no soporta (APIs, eventos,
/// XAML), sin datos personales, para generar un informe de compatibilidad.
/// </summary>
public sealed class InformeDeCompatibilidad
{
    private readonly List<(string Categoria, string Detalle)> _entradas = new();

    public IReadOnlyList<(string Categoria, string Detalle)> Entradas => _entradas;

    public void Anotar(string categoria, string detalle)
    {
        if (!_entradas.Contains((categoria, detalle))) _entradas.Add((categoria, detalle));
    }

    public string AJson(string app, string version) => JsonSerializer.Serialize(new
    {
        app,
        version,
        plata = typeof(InformeDeCompatibilidad).Assembly.GetName().Version?.ToString(),
        faltantes = _entradas.Select(e => new { categoria = e.Categoria, detalle = e.Detalle }),
    });
}
