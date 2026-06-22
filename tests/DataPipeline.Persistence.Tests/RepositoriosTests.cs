using DataPipeline.Core.Pipelines;
using DataPipeline.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataPipeline.Persistence.Tests;

public class RepositoriosTests
{
    private static MetadataDbContext NuevoCtx()
    {
        var cs = $"Data Source={Path.Combine(Path.GetTempPath(), $"meta_{Guid.NewGuid():N}.db")};Pooling=False";
        var opts = new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(cs).Options;
        var ctx = new MetadataDbContext(opts);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static PipelineDefinition Def(string nombre, DateTime? proxima, bool habilitado = true) => new()
    {
        Id = Guid.NewGuid(),
        Nombre = nombre,
        Habilitado = habilitado,
        Programacion = TipoProgramacion.Diaria,
        ProximaCorrida = proxima,
        TablaDestino = "t",
        ModoEscritura = ModoEscritura.Reemplazar,
        FilasPorLote = 1000,
        Pasos = { new StepDefinition { Nombre = "leer", Tipo = TipoPaso.Origen, Conector = "SqlServerSource" } }
    };

    [Fact]
    public async Task Upsert_y_GetByNombre_roundtrip_incluye_pasos()
    {
        using var ctx = NuevoCtx();
        var store = new EfPipelineStore(ctx);
        await store.UpsertAsync(Def("ventas", null), CancellationToken.None);

        var leido = await store.GetByNombreAsync("ventas", CancellationToken.None);

        Assert.NotNull(leido);
        Assert.Single(leido!.Pasos);
        Assert.Equal("leer", leido.Pasos[0].Nombre);
    }

    [Fact]
    public async Task ObtenerVencidos_filtra_por_habilitado_y_proxima()
    {
        using var ctx = NuevoCtx();
        var store = new EfPipelineStore(ctx);
        var ahora = new DateTime(2026, 6, 20, 12, 0, 0);
        await store.UpsertAsync(Def("vencido", ahora.AddMinutes(-1)), CancellationToken.None);
        await store.UpsertAsync(Def("futuro", ahora.AddHours(1)), CancellationToken.None);
        await store.UpsertAsync(Def("deshabilitado", ahora.AddMinutes(-1), habilitado: false), CancellationToken.None);
        await store.UpsertAsync(Def("ondemand", null), CancellationToken.None);

        var vencidos = await store.ObtenerVencidosAsync(ahora, CancellationToken.None);

        Assert.Single(vencidos);
        Assert.Equal("vencido", vencidos[0].Nombre);
    }

    [Fact]
    public async Task Upsert_roundtrip_incluye_columnas_clave()
    {
        using var ctx = NuevoCtx();
        var store = new EfPipelineStore(ctx);
        var def = Def("conclave", null);
        def.ColumnasClave = new() { "id_venta" };
        await store.UpsertAsync(def, CancellationToken.None);

        var leido = await store.GetByNombreAsync("conclave", CancellationToken.None);

        Assert.NotNull(leido);
        Assert.Equal(new[] { "id_venta" }, leido!.ColumnasClave.ToArray());
    }

    [Fact]
    public async Task Historial_iniciar_completar_y_recientes()
    {
        using var ctx = NuevoCtx();
        var hist = new EfHistorialCorridas(ctx);
        var pid = Guid.NewGuid();

        var runId = await hist.IniciarAsync(pid, CancellationToken.None);
        await hist.CompletarAsync(runId, exito: true, leidas: 10, escritas: 8, error: null, CancellationToken.None);

        var recientes = await hist.ObtenerRecientesAsync(10, CancellationToken.None);
        Assert.Single(recientes);
        Assert.Equal(EstadoCorrida.Ok, recientes[0].Estado);
        Assert.Equal(8, recientes[0].FilasEscritas);
    }

    [Fact]
    public async Task Delete_borra_y_devuelve_true_o_false()
    {
        using var ctx = NuevoCtx();
        var store = new EfPipelineStore(ctx);
        var def = Def("borrar", null);
        await store.UpsertAsync(def, CancellationToken.None);

        var borrado = await store.DeleteAsync(def.Id, CancellationToken.None);
        var leido = await store.GetByIdAsync(def.Id, CancellationToken.None);
        var inexistente = await store.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(borrado);
        Assert.Null(leido);
        Assert.False(inexistente);
    }
}
