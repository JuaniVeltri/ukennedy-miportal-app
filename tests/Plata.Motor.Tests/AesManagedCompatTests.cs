// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
using Xunit;
using AesCompat = Plata.Compat.AesManaged;

public class AesManagedCompatTests
{
    // FIPS-197, apéndice C: un bloque, sin relleno (CBC con IV en cero = ECB).
    [Theory]
    [InlineData("000102030405060708090a0b0c0d0e0f", "69c4e0d86a7b0430d8cdb78070b4c55a")]
    [InlineData("000102030405060708090a0b0c0d0e0f1011121314151617", "dda97ca4864cdfe06eaf70a0ec0d7191")]
    [InlineData("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "8ea2b7ca516745bfeafc49904b496089")]
    public void VectorFips197(string clave, string cifradoEsperado)
    {
        var plano = Convert.FromHexString("00112233445566778899aabbccddeeff");
        using var aes = new AesCompat { Key = Convert.FromHexString(clave), IV = new byte[16], Padding = PaddingMode.None };

        var cifrado = aes.CreateEncryptor().TransformFinalBlock(plano, 0, plano.Length);
        Assert.Equal(cifradoEsperado, Convert.ToHexString(cifrado).ToLowerInvariant());

        var descifrado = aes.CreateDecryptor().TransformFinalBlock(cifrado, 0, cifrado.Length);
        Assert.Equal(plano, descifrado);
    }

    // Mismo resultado que el AES del sistema (en escritorio sí existe), pasando por
    // CryptoStream como lo usan las apps: CBC + PKCS7, varias longitudes y claves.
    [Fact]
    public void CoincideConElAesDelSistema()
    {
        var azar = new Random(1234);
        foreach (var bitsClave in new[] { 128, 192, 256 })
            for (var largo = 0; largo <= 70; largo++)
            {
                var clave = Bytes(azar, bitsClave / 8);
                var iv = Bytes(azar, 16);
                var plano = Bytes(azar, largo);

                using var sistema = Aes.Create();
                sistema.Key = clave; sistema.IV = iv;
                using var compat = new AesCompat { Key = clave, IV = iv };

                var esperado = Transformar(sistema.CreateEncryptor(), plano);
                Assert.Equal(esperado, Transformar(compat.CreateEncryptor(), plano));
                Assert.Equal(plano, Transformar(compat.CreateDecryptor(), esperado));
            }
    }

    [Fact]
    public void RellenoInvalido_falla()
    {
        // Un bloque cuyo último byte es 0: nunca es un relleno PKCS7 válido.
        using var sinRelleno = new AesCompat { Key = new byte[16], IV = new byte[16], Padding = PaddingMode.None };
        var cifrado = sinRelleno.CreateEncryptor().TransformFinalBlock(new byte[16], 0, 16);

        using var aes = new AesCompat { Key = new byte[16], IV = new byte[16] };
        Assert.Throws<CryptographicException>(() => aes.CreateDecryptor().TransformFinalBlock(cifrado, 0, 16));
    }

    private static byte[] Transformar(ICryptoTransform t, byte[] datos)
    {
        using var salida = new MemoryStream();
        using (var cs = new CryptoStream(salida, t, CryptoStreamMode.Write))
        {
            // De a pedazos irregulares, para ejercitar TransformBlock y el bloque retenido.
            for (var i = 0; i < datos.Length; i += 7)
                cs.Write(datos, i, Math.Min(7, datos.Length - i));
        }
        return salida.ToArray();
    }

    private static byte[] Bytes(Random azar, int n)
    {
        var b = new byte[n];
        azar.NextBytes(b);
        return b;
    }
}
