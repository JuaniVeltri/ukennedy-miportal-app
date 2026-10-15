// SPDX-License-Identifier: MIT
using Plata.Host;
using Xunit;

public class CredencialesTests
{
    // En macOS "Recordarme" no existe: la app nunca toca el Llavero (que pide permiso) ni guarda la
    // cuenta en otro lado. Corre en el CI de macOS; en Linux no hace nada (no toca libsecret).
    [Fact]
    public void EnMacOS_noEstaDisponibleNiGuardaNada()
    {
        if (!OperatingSystem.IsMacOS()) return;

        Credenciales.Guardar("prueba.ejemplo.edu.ar", new Cuenta("alumno", "secreta"));

        Assert.False(Credenciales.Disponible);
        Assert.Null(Credenciales.Leer("prueba.ejemplo.edu.ar"));
    }
}
