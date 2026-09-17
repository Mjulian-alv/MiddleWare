// Fecha: 17/09/2026 - TAREA: 00038 - Linea: 1 - Envio a SQL externo: persistencia de envios.
// El test de la toma anti-solape es el que importa: dos barridos concurrentes no pueden llevarse
// el mismo envio.
using DataPipeline.Core.Envios;
using DataPipeline.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataPipeline.Persistence.Tests;

public class EnvioStoreTests
{
    private static MetadataDbContext NuevoMeta()
    {
        var cs = $"Data Source={Path.Combine(Path.GetTempPath(), $"envsto_{Guid.NewGuid():N}.db")};Pooling=False";
        var ctx = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(cs).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static Envio NuevoEnvio(Guid pipelineId, string nombre = "e1") => new()
    {
        Id = Guid.NewGuid(), Nombre = nombre, Habilitado = true,
        PipelineId = pipelineId, DataSourceId = Guid.NewGuid(), TablaDestino = "dbo.mw_x"
    };

    [Fact]
    public async Task Marcar_pendientes_prende_solo_los_envios_habilitados_de_ese_pipeline()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var pipeA = Guid.NewGuid();
        var pipeB = Guid.NewGuid();

        var deA = NuevoEnvio(pipeA, "a");
        var apagado = NuevoEnvio(pipeA, "apagado");
        apagado.Habilitado = false;
        var deB = NuevoEnvio(pipeB, "b");
        foreach (var e in new[] { deA, apagado, deB }) await store.UpsertAsync(e, CancellationToken.None);

        var n = await store.MarcarPendientesAsync(pipeA, CancellationToken.None);

        Assert.Equal(1, n);
        Assert.True((await store.GetByIdAsync(deA.Id, CancellationToken.None))!.Pendiente);
        Assert.False((await store.GetByIdAsync(apagado.Id, CancellationToken.None))!.Pendiente);
        Assert.False((await store.GetByIdAsync(deB.Id, CancellationToken.None))!.Pendiente);
    }

    [Fact]
    public async Task Tomar_pendientes_devuelve_el_envio_y_lo_deja_en_curso()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Pendiente = true;
        await store.UpsertAsync(e, CancellationToken.None);
        var ahora = new DateTime(2026, 9, 12, 3, 4, 0);

        var tomados = await store.TomarPendientesAsync(ahora, CancellationToken.None);

        Assert.Single(tomados);
        Assert.Equal(ahora, (await store.GetByIdAsync(e.Id, CancellationToken.None))!.InicioEnCurso);
    }

    [Fact]
    public async Task Tomar_pendientes_no_se_lleva_dos_veces_el_mismo_envio()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Pendiente = true;
        await store.UpsertAsync(e, CancellationToken.None);
        var ahora = new DateTime(2026, 9, 12, 3, 4, 0);

        var primero = await store.TomarPendientesAsync(ahora, CancellationToken.None);
        var segundo = await store.TomarPendientesAsync(ahora.AddSeconds(60), CancellationToken.None);

        Assert.Single(primero);
        Assert.Empty(segundo);
    }

    [Fact]
    public async Task Tomar_pendientes_respeta_el_ProximoIntento_que_todavia_no_vencio()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var ahora = new DateTime(2026, 9, 12, 3, 4, 0);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Pendiente = true;
        e.ProximoIntento = ahora.AddMinutes(5);
        await store.UpsertAsync(e, CancellationToken.None);

        Assert.Empty(await store.TomarPendientesAsync(ahora, CancellationToken.None));
        Assert.Single(await store.TomarPendientesAsync(ahora.AddMinutes(6), CancellationToken.None));
    }

    [Fact]
    public async Task Registrar_exito_avanza_el_cursor_y_apaga_la_bandera()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Pendiente = true;
        e.IntentosFallidos = 3;
        e.UltimoError = "algo";
        await store.UpsertAsync(e, CancellationToken.None);
        var cursor = new DateTime(2026, 9, 12, 3, 3, 58);
        var fin = new DateTime(2026, 9, 12, 3, 4, 2);

        await store.RegistrarExitoAsync(e.Id, cursor, fin, CancellationToken.None);

        var r = (await store.GetByIdAsync(e.Id, CancellationToken.None))!;
        Assert.Equal(cursor, r.UltimoCreado);
        Assert.Equal(fin, r.UltimoEnvio);
        Assert.False(r.Pendiente);
        Assert.Null(r.InicioEnCurso);
        Assert.Equal(0, r.IntentosFallidos);
        Assert.Null(r.UltimoError);
        Assert.Equal(EstadoEnvio.AlDia, r.Estado);
    }

    [Fact]
    public async Task Registrar_exito_sin_cursor_no_inventa_una_fecha()
    {
        // Sin filas nuevas en el primer envio no hay cursor que guardar: tiene que quedar null y no
        // DateTime.MinValue, que la pantalla mostraria como 01/01/0001.
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Pendiente = true;
        await store.UpsertAsync(e, CancellationToken.None);

        await store.RegistrarExitoAsync(e.Id, null, new DateTime(2026, 9, 12, 3, 4, 2), CancellationToken.None);

        Assert.Null((await store.GetByIdAsync(e.Id, CancellationToken.None))!.UltimoCreado);
    }

    [Fact]
    public async Task Registrar_fallo_suma_intento_y_deja_la_bandera_prendida()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Pendiente = true;
        await store.UpsertAsync(e, CancellationToken.None);
        var proximo = new DateTime(2026, 9, 12, 3, 5, 0);

        await store.RegistrarFalloAsync(e.Id, "no conecta", proximo, false, CancellationToken.None);

        var r = (await store.GetByIdAsync(e.Id, CancellationToken.None))!;
        Assert.Equal(1, r.IntentosFallidos);
        Assert.Equal("no conecta", r.UltimoError);
        Assert.Equal(proximo, r.ProximoIntento);
        Assert.True(r.Pendiente);
        Assert.Null(r.InicioEnCurso);
        Assert.True(r.Habilitado);
    }

    [Fact]
    public async Task Registrar_fallo_con_deshabilitar_apaga_el_envio()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Pendiente = true;
        e.IntentosFallidos = Envio.MaxIntentos - 1;
        await store.UpsertAsync(e, CancellationToken.None);

        await store.RegistrarFalloAsync(e.Id, "login failed", null, true, CancellationToken.None);

        var r = (await store.GetByIdAsync(e.Id, CancellationToken.None))!;
        Assert.False(r.Habilitado);
        Assert.Equal(Envio.MaxIntentos, r.IntentosFallidos);
        Assert.Equal(EstadoEnvio.Fallido, r.Estado);
    }

    [Fact]
    public async Task Reactivar_limpia_los_intentos_y_vuelve_a_habilitar()
    {
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        e.Habilitado = false;
        e.IntentosFallidos = Envio.MaxIntentos;
        e.UltimoError = "login failed";
        await store.UpsertAsync(e, CancellationToken.None);

        await store.ReactivarAsync(e.Id, CancellationToken.None);

        var r = (await store.GetByIdAsync(e.Id, CancellationToken.None))!;
        Assert.True(r.Habilitado);
        Assert.Equal(0, r.IntentosFallidos);
        Assert.Null(r.ProximoIntento);
        Assert.True(r.Pendiente);
    }

    [Fact]
    public async Task Se_puede_leer_modificar_y_volver_a_guardar()
    {
        // Guarda contra el conflicto de tracking: leer, tocar una propiedad y reguardar la misma
        // entidad no puede tirar "another instance with the same key is already being tracked".
        using var meta = NuevoMeta();
        var store = new EfEnvioStore(meta);
        var e = NuevoEnvio(Guid.NewGuid());
        await store.UpsertAsync(e, CancellationToken.None);

        var leido = (await store.GetByIdAsync(e.Id, CancellationToken.None))!;
        leido.TablaDestino = "dbo.otra";
        await store.UpsertAsync(leido, CancellationToken.None);

        Assert.Equal("dbo.otra", (await store.GetByIdAsync(e.Id, CancellationToken.None))!.TablaDestino);
    }
}
