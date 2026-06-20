namespace DataPipeline.Core.Security;

/// <summary>Contrato de encriptación AES-256 (idéntico al de ConsultasGenericas).</summary>
public interface IEncryptionService
{
    string Encrypt(string text);
    string Decrypt(string encryptedText);
}
