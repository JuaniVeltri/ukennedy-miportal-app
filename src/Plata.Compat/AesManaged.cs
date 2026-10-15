// SPDX-License-Identifier: MIT
using System.Security.Cryptography;

namespace Plata.Compat;

/// <summary>
/// Reemplazo del <c>AesManaged</c> de Silverlight. .NET en WebAssembly no trae AES
/// (<see cref="PlatformNotSupportedException"/>), así que el traductor redirige las apps
/// a esta implementación administrada. Como el de Silverlight, solo hace CBC (con
/// relleno PKCS7 o sin relleno).
/// </summary>
/// <remarks>
/// Existe por compatibilidad, no por seguridad: no busca resistir ataques de tiempo.
/// </remarks>
public sealed class AesManaged : Aes
{
    public override ICryptoTransform CreateEncryptor(byte[] rgbKey, byte[]? rgbIV)
        => new TransformacionAesCbc(rgbKey, rgbIV, cifrar: true, Mode, Padding);

    public override ICryptoTransform CreateDecryptor(byte[] rgbKey, byte[]? rgbIV)
        => new TransformacionAesCbc(rgbKey, rgbIV, cifrar: false, Mode, Padding);

    public override void GenerateKey() => KeyValue = RandomNumberGenerator.GetBytes(KeySizeValue / 8);

    public override void GenerateIV() => IVValue = RandomNumberGenerator.GetBytes(BlockSizeValue / 8);
}

internal sealed class TransformacionAesCbc : ICryptoTransform
{
    private const int Bloque = 16;
    private readonly BloqueAes _aes;
    private readonly bool _cifrar;
    private readonly bool _pkcs7;
    private readonly byte[] _previo = new byte[Bloque];
    // Al descifrar con relleno, el último bloque se retiene hasta TransformFinalBlock,
    // porque puede ser el que trae el relleno a quitar.
    private byte[]? _retenido;

    public TransformacionAesCbc(byte[] clave, byte[]? iv, bool cifrar, CipherMode modo, PaddingMode relleno)
    {
        if (modo != CipherMode.CBC)
            throw new NotSupportedException($"AES: solo se admite CBC (pedido: {modo}).");
        if (relleno is not (PaddingMode.PKCS7 or PaddingMode.None))
            throw new NotSupportedException($"AES: solo se admite relleno PKCS7 o ninguno (pedido: {relleno}).");
        if (iv is null || iv.Length != Bloque)
            throw new CryptographicException("AES: el IV debe tener 16 bytes.");
        _aes = new BloqueAes(clave);
        _cifrar = cifrar;
        _pkcs7 = relleno == PaddingMode.PKCS7;
        iv.CopyTo(_previo, 0);
    }

    public int InputBlockSize => Bloque;
    public int OutputBlockSize => Bloque;
    public bool CanTransformMultipleBlocks => true;
    public bool CanReuseTransform => false;

    public int TransformBlock(byte[] entrada, int desde, int cantidad, byte[] salida, int hasta)
    {
        if (cantidad % Bloque != 0) throw new CryptographicException("AES: bloque incompleto.");
        if (cantidad == 0) return 0;
        if (_cifrar)
        {
            Cifrar(entrada, desde, cantidad, salida, hasta);
            return cantidad;
        }

        var escritos = 0;
        if (_retenido is not null)
        {
            Buffer.BlockCopy(_retenido, 0, salida, hasta, Bloque);
            escritos = Bloque;
            _retenido = null;
        }
        var directos = _pkcs7 ? cantidad - Bloque : cantidad;
        Descifrar(entrada, desde, directos, salida, hasta + escritos);
        if (_pkcs7)
        {
            _retenido = new byte[Bloque];
            Descifrar(entrada, desde + directos, Bloque, _retenido, 0);
        }
        return escritos + directos;
    }

    public byte[] TransformFinalBlock(byte[] entrada, int desde, int cantidad)
    {
        if (_cifrar)
        {
            if (!_pkcs7 && cantidad % Bloque != 0) throw new CryptographicException("AES: bloque incompleto.");
            var relleno = _pkcs7 ? Bloque - cantidad % Bloque : 0;
            var datos = new byte[cantidad + relleno];
            Buffer.BlockCopy(entrada, desde, datos, 0, cantidad);
            datos.AsSpan(cantidad).Fill((byte)relleno);
            Cifrar(datos, 0, datos.Length, datos, 0);
            return datos;
        }

        if (cantidad % Bloque != 0) throw new CryptographicException("AES: largo de datos cifrados inválido.");
        var previos = _retenido is null ? 0 : Bloque;
        var plano = new byte[previos + cantidad];
        if (_retenido is not null) Buffer.BlockCopy(_retenido, 0, plano, 0, Bloque);
        _retenido = null;
        Descifrar(entrada, desde, cantidad, plano, previos);
        if (!_pkcs7) return plano;

        var r = plano.Length == 0 ? 0 : plano[^1];
        if (r is < 1 or > Bloque || r > plano.Length)
            throw new CryptographicException("AES: relleno inválido.");
        for (var i = plano.Length - r; i < plano.Length; i++)
            if (plano[i] != r) throw new CryptographicException("AES: relleno inválido.");
        return plano[..^r];
    }

    private void Cifrar(byte[] entrada, int desde, int cantidad, byte[] salida, int hasta)
    {
        var b = new byte[Bloque];
        for (var o = 0; o < cantidad; o += Bloque)
        {
            for (var i = 0; i < Bloque; i++) b[i] = (byte)(entrada[desde + o + i] ^ _previo[i]);
            _aes.Cifrar(b, _previo);
            Buffer.BlockCopy(_previo, 0, salida, hasta + o, Bloque);
        }
    }

    private void Descifrar(byte[] entrada, int desde, int cantidad, byte[] salida, int hasta)
    {
        var c = new byte[Bloque];
        var p = new byte[Bloque];
        for (var o = 0; o < cantidad; o += Bloque)
        {
            Buffer.BlockCopy(entrada, desde + o, c, 0, Bloque);
            _aes.Descifrar(c, p);
            for (var i = 0; i < Bloque; i++) salida[hasta + o + i] = (byte)(p[i] ^ _previo[i]);
            Buffer.BlockCopy(c, 0, _previo, 0, Bloque);
        }
    }

    public void Dispose()
    {
        Array.Clear(_previo);
        if (_retenido is not null) Array.Clear(_retenido);
    }
}

/// <summary>El cifrador de bloque AES (FIPS-197), un bloque de 16 bytes por vez.</summary>
internal sealed class BloqueAes
{
    private static readonly byte[] S = new byte[256];
    private static readonly byte[] SInv = new byte[256];

    private readonly byte[] _claves; // clave de cada ronda, 16 bytes por ronda
    private readonly int _rondas;

    static BloqueAes()
    {
        // Caja S: inverso multiplicativo en GF(2^8) seguido de la transformación afín.
        byte p = 1, q = 1;
        do
        {
            p = (byte)(p ^ (p << 1) ^ ((p & 0x80) != 0 ? 0x1B : 0)); // p * 3
            q ^= (byte)(q << 1); // q / 3
            q ^= (byte)(q << 2);
            q ^= (byte)(q << 4);
            if ((q & 0x80) != 0) q ^= 0x09;
            S[p] = (byte)(q ^ Rotar(q, 1) ^ Rotar(q, 2) ^ Rotar(q, 3) ^ Rotar(q, 4) ^ 0x63);
        } while (p != 1);
        S[0] = 0x63;
        for (var i = 0; i < 256; i++) SInv[S[i]] = (byte)i;
    }

    public BloqueAes(byte[] clave)
    {
        if (clave is null || clave.Length is not (16 or 24 or 32))
            throw new CryptographicException("AES: la clave debe tener 16, 24 o 32 bytes.");
        var nk = clave.Length / 4;
        _rondas = nk + 6;
        _claves = new byte[16 * (_rondas + 1)];
        Buffer.BlockCopy(clave, 0, _claves, 0, clave.Length);

        var t = new byte[4];
        byte rcon = 1;
        for (var i = nk; i < 4 * (_rondas + 1); i++)
        {
            Buffer.BlockCopy(_claves, (i - 1) * 4, t, 0, 4);
            if (i % nk == 0)
            {
                var t0 = t[0]; // RotWord + SubWord + Rcon
                t[0] = (byte)(S[t[1]] ^ rcon);
                t[1] = S[t[2]];
                t[2] = S[t[3]];
                t[3] = S[t0];
                rcon = Xtime(rcon);
            }
            else if (nk > 6 && i % nk == 4)
            {
                for (var j = 0; j < 4; j++) t[j] = S[t[j]];
            }
            for (var j = 0; j < 4; j++) _claves[i * 4 + j] = (byte)(_claves[(i - nk) * 4 + j] ^ t[j]);
        }
    }

    // El estado es columna por columna: byte (fila r, columna c) = s[r + 4c].
    public void Cifrar(byte[] entrada, byte[] salida)
    {
        var s = (byte[])entrada.Clone();
        SumarClave(s, 0);
        for (var r = 1; r < _rondas; r++)
        {
            Sustituir(s, S);
            DesplazarFilas(s, inversa: false);
            MezclarColumnas(s);
            SumarClave(s, r);
        }
        Sustituir(s, S);
        DesplazarFilas(s, inversa: false);
        SumarClave(s, _rondas);
        Buffer.BlockCopy(s, 0, salida, 0, 16);
    }

    public void Descifrar(byte[] entrada, byte[] salida)
    {
        var s = (byte[])entrada.Clone();
        SumarClave(s, _rondas);
        for (var r = _rondas - 1; r >= 1; r--)
        {
            DesplazarFilas(s, inversa: true);
            Sustituir(s, SInv);
            SumarClave(s, r);
            MezclarColumnasInversa(s);
        }
        DesplazarFilas(s, inversa: true);
        Sustituir(s, SInv);
        SumarClave(s, 0);
        Buffer.BlockCopy(s, 0, salida, 0, 16);
    }

    private void SumarClave(byte[] s, int ronda)
    {
        for (var i = 0; i < 16; i++) s[i] ^= _claves[16 * ronda + i];
    }

    private static void Sustituir(byte[] s, byte[] caja)
    {
        for (var i = 0; i < 16; i++) s[i] = caja[s[i]];
    }

    // Fila r rota r lugares a la izquierda (o a la derecha, si es la inversa).
    private static void DesplazarFilas(byte[] s, bool inversa)
    {
        var t = (byte[])s.Clone();
        for (var c = 0; c < 4; c++)
            for (var r = 1; r < 4; r++)
                s[r + 4 * c] = t[r + 4 * ((inversa ? c - r + 4 : c + r) % 4)];
    }

    private static void MezclarColumnas(byte[] s)
    {
        for (var c = 0; c < 16; c += 4)
        {
            byte a0 = s[c], a1 = s[c + 1], a2 = s[c + 2], a3 = s[c + 3];
            s[c] = (byte)(Mul(a0, 2) ^ Mul(a1, 3) ^ a2 ^ a3);
            s[c + 1] = (byte)(a0 ^ Mul(a1, 2) ^ Mul(a2, 3) ^ a3);
            s[c + 2] = (byte)(a0 ^ a1 ^ Mul(a2, 2) ^ Mul(a3, 3));
            s[c + 3] = (byte)(Mul(a0, 3) ^ a1 ^ a2 ^ Mul(a3, 2));
        }
    }

    private static void MezclarColumnasInversa(byte[] s)
    {
        for (var c = 0; c < 16; c += 4)
        {
            byte a0 = s[c], a1 = s[c + 1], a2 = s[c + 2], a3 = s[c + 3];
            s[c] = (byte)(Mul(a0, 14) ^ Mul(a1, 11) ^ Mul(a2, 13) ^ Mul(a3, 9));
            s[c + 1] = (byte)(Mul(a0, 9) ^ Mul(a1, 14) ^ Mul(a2, 11) ^ Mul(a3, 13));
            s[c + 2] = (byte)(Mul(a0, 13) ^ Mul(a1, 9) ^ Mul(a2, 14) ^ Mul(a3, 11));
            s[c + 3] = (byte)(Mul(a0, 11) ^ Mul(a1, 13) ^ Mul(a2, 9) ^ Mul(a3, 14));
        }
    }

    // Producto en GF(2^8) con el polinomio de AES.
    private static byte Mul(byte a, byte b)
    {
        byte p = 0;
        while (b != 0)
        {
            if ((b & 1) != 0) p ^= a;
            a = Xtime(a);
            b >>= 1;
        }
        return p;
    }

    private static byte Xtime(byte a) => (byte)((a << 1) ^ ((a & 0x80) != 0 ? 0x1B : 0));

    private static byte Rotar(byte x, int n) => (byte)((x << n) | (x >> (8 - n)));
}
