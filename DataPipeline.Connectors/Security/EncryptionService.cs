using System.Security.Cryptography;
using System.Text;
using DataPipeline.Core.Security;

namespace DataPipeline.Connectors.Security;

/// <summary>Encriptación AES-256 CBC con IV aleatorio. Clave de 32 bytes en base64.</summary>
public sealed class EncryptionService : IEncryptionService
{
    private readonly byte[] _clave;

    public EncryptionService(string claveBase64)
    {
        if (string.IsNullOrWhiteSpace(claveBase64))
            throw new ArgumentException("La clave de encriptación es obligatoria.", nameof(claveBase64));

        _clave = Convert.FromBase64String(claveBase64);

        if (_clave.Length != 32)
            throw new InvalidOperationException("La clave debe tener exactamente 32 bytes (AES-256).");
    }

    public string Encrypt(string text)
    {
        using var aes = Aes.Create();
        aes.Key = _clave;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(text);
        var cifrado = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);

        var resultado = new byte[aes.IV.Length + cifrado.Length];
        Array.Copy(aes.IV, 0, resultado, 0, aes.IV.Length);
        Array.Copy(cifrado, 0, resultado, aes.IV.Length, cifrado.Length);
        return Convert.ToBase64String(resultado);
    }

    public string Decrypt(string encryptedText)
    {
        var datos = Convert.FromBase64String(encryptedText);

        using var aes = Aes.Create();
        aes.Key = _clave;

        var iv = new byte[aes.BlockSize / 8];
        var cifrado = new byte[datos.Length - iv.Length];
        Array.Copy(datos, 0, iv, 0, iv.Length);
        Array.Copy(datos, iv.Length, cifrado, 0, cifrado.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var bytes = decryptor.TransformFinalBlock(cifrado, 0, cifrado.Length);
        return Encoding.UTF8.GetString(bytes);
    }
}
