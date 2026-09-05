// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Tests de CsvSource con archivos reales en disco:
// separador/encoding no default, sin encabezado, y watermark end-to-end.
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
using DataPipeline.Core.Security;
using DataPipeline.Connectors.Security;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class CsvSourceTests
{
    private static RunContext Ctx(Dictionary<string, string>? parametros = null) => new()
    {
        PipelineId = Guid.NewGuid(), RunId = Guid.NewGuid(),
        Parameters = parametros ?? new Dictionary<string, string>(),
        Encryption = new EncryptionService(Convert.ToBase64String(new byte[32])),
        BatchSize = 100
    };

    private static string NuevaCarpeta()
    {
        var d = Path.Combine(Path.GetTempPath(), $"csvsrc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private static async Task<List<Record>> Drenar(IAsyncEnumerable<RecordBatch> s)
    {
        var todas = new List<Record>();
        await foreach (var b in s) todas.AddRange(b.Rows);
        return todas;
    }

    [Fact]
    public async Task Lee_un_csv_con_punto_y_coma_y_encabezado()
    {
        var carpeta = NuevaCarpeta();
        File.WriteAllText(Path.Combine(carpeta, "ventas.csv"), "id;importe\n1;100\n2;200\n");
        var source = new CsvSource(carpeta, "*.csv", null, null, ";", "UTF-8", true);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Equal(2, filas.Count);
        Assert.Equal("100", filas[0].Values["importe"]);
    }

    [Fact]
    public async Task Sin_encabezado_nombra_las_columnas_Col1_ColN()
    {
        var carpeta = NuevaCarpeta();
        File.WriteAllText(Path.Combine(carpeta, "datos.csv"), "1,100\n2,200\n");
        var source = new CsvSource(carpeta, "*.csv", null, null, ",", "UTF-8", false);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Equal(2, filas.Count);
        Assert.Equal("100", filas[0].Values["Col2"]);
    }

    [Fact]
    public async Task Respeta_comillas_con_el_separador_adentro()
    {
        var carpeta = NuevaCarpeta();
        File.WriteAllText(Path.Combine(carpeta, "ventas.csv"), "id,nombre\n1,\"Perez, Juan\"\n");
        var source = new CsvSource(carpeta, "*.csv", null, null, ",", "UTF-8", true);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Equal("Perez, Juan", filas[0].Values["nombre"]);
    }

    [Fact]
    public async Task Filtra_por_watermark_tipado_como_int_a_traves_de_dos_archivos()
    {
        var carpeta = NuevaCarpeta();
        File.WriteAllText(Path.Combine(carpeta, "a_20260901.csv"), "id\n1\n2\n");
        File.SetLastWriteTimeUtc(Path.Combine(carpeta, "a_20260901.csv"), new DateTime(2026, 9, 1));
        File.WriteAllText(Path.Combine(carpeta, "b_20260902.csv"), "id\n2\n3\n");
        File.SetLastWriteTimeUtc(Path.Combine(carpeta, "b_20260902.csv"), new DateTime(2026, 9, 2));
        var source = new CsvSource(carpeta, "*.csv", "id", "int", ",", "UTF-8", true);

        // Primera corrida: sin watermark previo, trae todo (incluye repetidos entre archivos: caso "dump").
        var primera = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));
        Assert.Equal(4, primera.Count);

        // Ambos archivos ya se movieron a procesados: una segunda corrida sobre la misma carpeta no trae nada mas.
        var segunda = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));
        Assert.Empty(segunda);
    }

    // Fecha: 05/09/2026 - TAREA: 00015 - Linea: 1 - Revision final (Hallazgo 2, Importante): un CSV
    // genuinamente vacio (0 bytes) hacia que csv.Read() devolviera false y ReadHeader() tirara
    // ReaderException; como MoverAProcesados solo corre despues de una lectura limpia, el archivo vacio
    // quedaba en la carpeta de entrada para siempre, fallando en cada corrida siguiente.
    [Fact]
    public async Task Un_archivo_vacio_no_explota_y_se_mueve_a_procesados()
    {
        var carpeta = NuevaCarpeta();
        File.WriteAllText(Path.Combine(carpeta, "vacio.csv"), "");
        var source = new CsvSource(carpeta, "*.csv", null, null, ",", "UTF-8", true);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Empty(filas);
        Assert.False(File.Exists(Path.Combine(carpeta, "vacio.csv")));
        Assert.True(File.Exists(Path.Combine(carpeta, "procesados", "vacio.csv")));
    }
}
