// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: la factory es la unica pieza del
// destino con contrato testeable sin servidor. Descifrar mal la cadena solo fallaria en runtime.
using DataPipeline.Connectors.Destinos;
using DataPipeline.Connectors.Security;
using DataPipeline.Core.DataSources;
using Xunit;

namespace DataPipeline.Tests;

public class DestinoFactoryTests
{
    private static readonly EncryptionService Enc = new(Convert.ToBase64String(new byte[32]));

    [Fact]
    public void Descifra_la_cadena_y_crea_un_destino_SqlServer()
    {
        var ds = new DataSource
        {
            Nombre = "SQL-CONTA", Tipo = TipoDataSource.SqlServer,
            ConnectionString = Enc.Encrypt("Server=x;Database=y;Trusted_Connection=True")
        };

        Assert.IsType<SqlServerDestino>(new DestinoFactory().Crear(ds, Enc));
    }

    [Theory]
    [InlineData(TipoDataSource.Csv)]
    [InlineData(TipoDataSource.Xlsx)]
    public void Rechaza_origenes_que_no_son_SqlServer(TipoDataSource tipo)
    {
        var ds = new DataSource { Nombre = "archivos", Tipo = tipo, RutaCarpeta = @"C:\x" };

        var ex = Assert.Throws<NotSupportedException>(() => new DestinoFactory().Crear(ds, Enc));
        Assert.Contains("solo SqlServer", ex.Message);
    }

    [Fact]
    public void Rechaza_un_origen_sin_cadena_de_conexion()
    {
        var ds = new DataSource { Nombre = "vacio", Tipo = TipoDataSource.SqlServer, ConnectionString = null };

        Assert.Throws<InvalidOperationException>(() => new DestinoFactory().Crear(ds, Enc));
    }
}
