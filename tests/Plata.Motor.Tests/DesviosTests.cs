// SPDX-License-Identifier: MIT
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Plata.Compat;
using Xunit;

public class DesviosTests
{
    [DataContract]
    public class Dato
    {
        [DataMember] public string? Data { get; set; }
    }

    [Fact]
    public void LeerJson_streamRecienEscrito_leeDesdeElPrincipio()
    {
        // Lo que hacen las apps de Silverlight: escriben y leen sin volver a la posición 0.
        var ms = new MemoryStream();
        var bytes = Encoding.UTF8.GetBytes("{\"Data\":\"hola\"}");
        ms.Write(bytes, 0, bytes.Length);

        var dato = Assert.IsType<Dato>(Desvios.LeerJsonDesdeElPrincipio(new DataContractJsonSerializer(typeof(Dato)), ms));

        Assert.Equal("hola", dato.Data);
    }

    [Fact]
    public void EnsambladoYModuloDinamicos_comoEnSilverlight_funcionan()
    {
        var ab = Desvios.DefinirEnsambladoDinamico(AppDomain.CurrentDomain,
            new System.Reflection.AssemblyName("PruebaDinamica"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var mb = Desvios.DefinirModuloDinamico(ab, "modulo", false);
        var tipo = mb.DefineType("Generado", System.Reflection.TypeAttributes.Public).CreateType();

        Assert.Equal("Generado", tipo.Name);
    }

    [Fact]
    public void LeerJson_streamEnElMedio_respetaLaPosicion()
    {
        var bytes = Encoding.UTF8.GetBytes("xx{\"Data\":\"hola\"}");
        var ms = new MemoryStream(bytes) { Position = 2 };

        var dato = Assert.IsType<Dato>(Desvios.LeerJsonDesdeElPrincipio(new DataContractJsonSerializer(typeof(Dato)), ms));

        Assert.Equal("hola", dato.Data);
    }
}
