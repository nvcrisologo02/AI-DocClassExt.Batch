using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DocumentIA.Batch.ClassificationLite.Services;

/// <summary>
/// Ofusca la Function Key de cada entorno antes de guardarla en <c>config.json</c>.
///
/// ATENCION: esto es OFUSCACION PORTABLE, no seguridad criptografica fuerte. La clave y la
/// sal estan embebidas en el propio ejecutable (constantes en el codigo), asi que cualquiera
/// con el binario puede recuperarlas y descifrar el valor. El objetivo es evitar que la key
/// viaje en texto plano dentro de un fichero de configuracion que se distribuye junto a la
/// app, no protegerla frente a un atacante que ya tiene el ejecutable.
/// </summary>
public static class KeyObfuscator
{
    public const string Prefix = "enc:";

    // Passphrase y sal constantes embebidas: es intencional (ofuscacion portable, ver
    // comentario de la clase), no un secreto que deba rotarse ni protegerse aparte.
    private const string Passphrase = "DocumentIA.Batch.ClassificationLite.KeyObfuscator.v1";

    private static readonly byte[] Salt =
    {
        0x4a, 0x1f, 0x8c, 0x3d, 0x9e, 0x02, 0x77, 0x5b,
        0xc4, 0x61, 0xae, 0x30, 0xf9, 0x86, 0x12, 0xd5
    };

    private const int Iterations = 100_000;
    private const int KeySizeBits = 256;
    private const int IvSizeBytes = 16;

    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return string.Empty;
        }

        if (plain.StartsWith(Prefix, StringComparison.Ordinal))
        {
            // Ya esta cifrado: no doble-cifrar.
            return plain;
        }

        using var aes = CreateAes();
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);

        using (var encryptor = aes.CreateEncryptor())
        using (var cryptoStream = new CryptoStream(ms, encryptor, CryptoStreamMode.Write, leaveOpen: true))
        {
            var plainBytes = Encoding.UTF8.GetBytes(plain);
            cryptoStream.Write(plainBytes, 0, plainBytes.Length);
            cryptoStream.FlushFinalBlock();
        }

        return Prefix + Convert.ToBase64String(ms.ToArray());
    }

    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return string.Empty;
        }

        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            // Retrocompatibilidad: config.json antiguos con la key en texto plano.
            return stored;
        }

        try
        {
            var payload = Convert.FromBase64String(stored[Prefix.Length..]);
            if (payload.Length < IvSizeBytes)
            {
                return string.Empty;
            }

            var iv = payload[..IvSizeBytes];
            var cipherText = payload[IvSizeBytes..];

            using var aes = CreateAes();
            aes.IV = iv;

            using var ms = new MemoryStream(cipherText);
            using var decryptor = aes.CreateDecryptor();
            using var cryptoStream = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var reader = new StreamReader(cryptoStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            // Base64 invalido o datos corruptos: no debe romper el arranque de la app.
            return string.Empty;
        }
    }

    private static Aes CreateAes()
    {
        var aes = Aes.Create();
        aes.KeySize = KeySizeBits;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var deriveBytes = new Rfc2898DeriveBytes(Passphrase, Salt, Iterations, HashAlgorithmName.SHA256);
        aes.Key = deriveBytes.GetBytes(KeySizeBits / 8);

        return aes;
    }
}
