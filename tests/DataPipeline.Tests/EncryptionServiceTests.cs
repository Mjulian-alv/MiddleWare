using DataPipeline.Connectors.Security;
using Xunit;

namespace DataPipeline.Tests;

public class EncryptionServiceTests
{
    private static string ClaveValida() => Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    [Fact]
    public void Encrypt_luego_Decrypt_devuelve_el_texto_original()
    {
        var svc = new EncryptionService(ClaveValida());
        var original = "Server=POSSERVER;Database=ventas;User Id=sa;Password=secreto;";

        var cifrado = svc.Encrypt(original);
        var descifrado = svc.Decrypt(cifrado);

        Assert.NotEqual(original, cifrado);
        Assert.Equal(original, descifrado);
    }

    [Fact]
    public void Constructor_con_clave_de_largo_invalido_lanza()
    {
        var claveCorta = Convert.ToBase64String(new byte[16]);
        Assert.Throws<InvalidOperationException>(() => new EncryptionService(claveCorta));
    }
}
