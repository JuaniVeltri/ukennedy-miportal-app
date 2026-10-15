// SPDX-License-Identifier: MIT
using System.Text;

namespace Plata.Host;

/// <summary>
/// Copia lo que el host escribe en la consola a un archivo (se reemplaza en cada arranque). El host
/// no registra contraseñas, valores de cookies ni direcciones con parámetros.
/// </summary>
public static class Registro
{
    public static void Iniciar(string ruta)
    {
        try
        {
            if (ruta.StartsWith("~/", StringComparison.Ordinal))
                ruta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ruta[2..]);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
            var archivo = new StreamWriter(ruta, append: false, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(new Doble(Console.Out, archivo));
            Console.WriteLine($"registro iniciado {DateTime.Now:yyyy-MM-dd HH:mm:ss}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("no se pudo abrir el registro: " + e.Message);
        }
    }

    private sealed class Doble(TextWriter consola, TextWriter archivo) : TextWriter
    {
        private readonly object _cerrojo = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char valor) { lock (_cerrojo) { consola.Write(valor); archivo.Write(valor); } }
        public override void Write(string? valor) { lock (_cerrojo) { consola.Write(valor); archivo.Write(valor); } }
        public override void WriteLine(string? valor) { lock (_cerrojo) { consola.WriteLine(valor); archivo.WriteLine(valor); } }
    }
}
