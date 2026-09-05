// Fecha: 03/09/2026 - TAREA: 00014 - Linea: 1 - Tests de la logica comun de origenes de archivo
// (descubrimiento, orden, filtrado/tipado de watermark, mover a procesados), con un stub que no
// depende de CsvHelper/ClosedXML.
using System.Runtime.CompilerServices;
using DataPipeline.Connectors.Sources;
using DataPipeline.Core.Records;
using DataPipeline.Core.Runtime;
using DataPipeline.Core.Security;
using DataPipeline.Connectors.Security;
using Xunit;
using Record = DataPipeline.Core.Records.Record;

namespace DataPipeline.Tests;

public class FileSourceBaseTests
{
    private sealed class FileSourceDePrueba : FileSourceBase
    {
        private readonly Dictionary<string, (string[] Cols, string[][] Filas)> _contenido;

        public FileSourceDePrueba(string rutaCarpeta, string? patronArchivo, string? columnaWatermark,
            string? tipoColumnaWatermark, Dictionary<string, (string[] Cols, string[][] Filas)> contenido)
            : base(rutaCarpeta, patronArchivo, columnaWatermark, tipoColumnaWatermark) => _contenido = contenido;

        protected override string PatronPorDefecto() => "*.txt";

        public override IReadOnlyList<string> LeerEncabezado(string rutaArchivo) =>
            _contenido[Path.GetFileName(rutaArchivo)].Cols;

        protected override async IAsyncEnumerable<Record> LeerFilasAsync(
            string rutaArchivo, IReadOnlyList<string> columnas, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            foreach (var fila in _contenido[Path.GetFileName(rutaArchivo)].Filas)
            {
                var valores = new Dictionary<string, object?>(columnas.Count);
                for (int i = 0; i < columnas.Count; i++) valores[columnas[i]] = fila[i];
                yield return new Record { Values = valores };
            }
        }
    }

    private static RunContext Ctx(Dictionary<string, string>? parametros = null) => new()
    {
        PipelineId = Guid.NewGuid(), RunId = Guid.NewGuid(),
        Parameters = parametros ?? new Dictionary<string, string>(),
        Encryption = new EncryptionService(Convert.ToBase64String(new byte[32])),
        BatchSize = 100
    };

    private static string NuevaCarpeta()
    {
        var d = Path.Combine(Path.GetTempPath(), $"filesrc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private static void CrearArchivo(string carpeta, string nombre, DateTime ultimaEscritura)
    {
        var ruta = Path.Combine(carpeta, nombre);
        File.WriteAllText(ruta, "");
        File.SetLastWriteTimeUtc(ruta, ultimaEscritura);
    }

    private static async Task<List<Record>> Drenar(IAsyncEnumerable<RecordBatch> s)
    {
        var todas = new List<Record>();
        await foreach (var b in s) todas.AddRange(b.Rows);
        return todas;
    }

    [Fact]
    public async Task Lee_los_archivos_en_orden_de_fecha_y_los_mueve_a_procesados()
    {
        var carpeta = NuevaCarpeta();
        CrearArchivo(carpeta, "b.txt", new DateTime(2026, 1, 2));
        CrearArchivo(carpeta, "a.txt", new DateTime(2026, 1, 1));
        var contenido = new Dictionary<string, (string[], string[][])>
        {
            ["a.txt"] = (new[] { "id" }, new[] { new[] { "1" } }),
            ["b.txt"] = (new[] { "id" }, new[] { new[] { "2" } })
        };
        var source = new FileSourceDePrueba(carpeta, "*.txt", null, null, contenido);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Equal(new[] { "1", "2" }, filas.Select(f => f.Values["id"]));
        Assert.False(File.Exists(Path.Combine(carpeta, "a.txt")));
        Assert.False(File.Exists(Path.Combine(carpeta, "b.txt")));
        Assert.True(File.Exists(Path.Combine(carpeta, "procesados", "a.txt")));
        Assert.True(File.Exists(Path.Combine(carpeta, "procesados", "b.txt")));
    }

    [Fact]
    public async Task No_reprocesa_un_archivo_que_ya_esta_en_procesados()
    {
        var carpeta = NuevaCarpeta();
        Directory.CreateDirectory(Path.Combine(carpeta, "procesados"));
        CrearArchivo(carpeta, "nuevo.txt", DateTime.UtcNow);
        File.WriteAllText(Path.Combine(carpeta, "procesados", "viejo.txt"), "");
        var contenido = new Dictionary<string, (string[], string[][])>
        {
            ["nuevo.txt"] = (new[] { "id" }, new[] { new[] { "1" } })
        };
        var source = new FileSourceDePrueba(carpeta, "*.txt", null, null, contenido);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Single(filas);
    }

    [Fact]
    public async Task Filtra_y_tipa_la_columna_watermark_como_fecha()
    {
        var carpeta = NuevaCarpeta();
        CrearArchivo(carpeta, "datos.txt", DateTime.UtcNow);
        var contenido = new Dictionary<string, (string[], string[][])>
        {
            ["datos.txt"] = (new[] { "id", "fecha" }, new[]
            {
                new[] { "1", "2026-01-01" },
                new[] { "2", "2026-01-10" },
                new[] { "3", "2026-01-05" },
            })
        };
        var source = new FileSourceDePrueba(carpeta, "*.txt", "fecha", "date", contenido);
        var ctx = Ctx(new Dictionary<string, string> { ["watermark"] = "2026-01-01T00:00:00.0000000" });

        var filas = await Drenar(source.ReadAsync(ctx, CancellationToken.None));

        // Solo pasan las filas con fecha > 2026-01-01 (id 2 y 3); el valor queda tipado (DateTime, no string).
        Assert.Equal(new[] { "2", "3" }, filas.Select(f => f.Values["id"]));
        Assert.All(filas, f => Assert.IsType<DateTime>(f.Values["fecha"]));
    }

    [Fact]
    public async Task Primera_corrida_sin_watermark_previo_no_filtra_nada()
    {
        var carpeta = NuevaCarpeta();
        CrearArchivo(carpeta, "datos.txt", DateTime.UtcNow);
        var contenido = new Dictionary<string, (string[], string[][])>
        {
            ["datos.txt"] = (new[] { "id", "fecha" }, new[]
            {
                new[] { "1", "2026-01-01" },
                new[] { "2", "2026-01-10" },
            })
        };
        var source = new FileSourceDePrueba(carpeta, "*.txt", "fecha", "date", contenido);

        var filas = await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Equal(2, filas.Count);
    }

    // Fecha: 05/09/2026 - TAREA: 00015 - Linea: 1 - Revision final (Hallazgo 3, Importante): una celda
    // en blanco ("") en la columna watermark (CSV con un campo vacio, o celda vacia de Excel en una
    // columna por lo demas poblada) tiraba FormatException al tipar, tumbando toda la corrida. Se trata
    // igual que "sin watermark configurado": la fila se sigue emitiendo, sin tipar/filtrar ese valor.
    [Fact]
    public async Task Una_celda_en_blanco_en_la_columna_watermark_no_tumba_la_corrida()
    {
        var carpeta = NuevaCarpeta();
        CrearArchivo(carpeta, "datos.txt", DateTime.UtcNow);
        var contenido = new Dictionary<string, (string[], string[][])>
        {
            ["datos.txt"] = (new[] { "id", "fecha" }, new[]
            {
                new[] { "1", "2026-01-01" },
                new[] { "2", "" },             // celda en blanco en la columna watermark
                new[] { "3", "2026-01-05" },
            })
        };
        var source = new FileSourceDePrueba(carpeta, "*.txt", "fecha", "date", contenido);
        var ctx = Ctx(new Dictionary<string, string> { ["watermark"] = "2026-01-01T00:00:00.0000000" });

        var filas = await Drenar(source.ReadAsync(ctx, CancellationToken.None));

        Assert.Equal(new[] { "2", "3" }, filas.Select(f => f.Values["id"]));
        var filaEnBlanco = filas.Single(f => (string)f.Values["id"]! == "2");
        Assert.Equal("", filaEnBlanco.Values["fecha"]); // no tipada: queda tal cual, en vez de tirar
    }

    [Fact]
    public async Task Un_archivo_con_el_mismo_nombre_ya_en_procesados_no_se_pisa()
    {
        var carpeta = NuevaCarpeta();
        Directory.CreateDirectory(Path.Combine(carpeta, "procesados"));
        File.WriteAllText(Path.Combine(carpeta, "procesados", "datos.txt"), "viejo");
        CrearArchivo(carpeta, "datos.txt", DateTime.UtcNow);
        var contenido = new Dictionary<string, (string[], string[][])>
        {
            ["datos.txt"] = (new[] { "id" }, new[] { new[] { "1" } })
        };
        var source = new FileSourceDePrueba(carpeta, "*.txt", null, null, contenido);

        await Drenar(source.ReadAsync(Ctx(), CancellationToken.None));

        Assert.Equal("viejo", File.ReadAllText(Path.Combine(carpeta, "procesados", "datos.txt")));
        Assert.Empty(Directory.GetFiles(carpeta, "*.txt")); // el nuevo no quedo en la carpeta de entrada
        Assert.Equal(2, Directory.GetFiles(Path.Combine(carpeta, "procesados")).Length); // viejo.txt + datos_<sufijo>.txt
    }
}
