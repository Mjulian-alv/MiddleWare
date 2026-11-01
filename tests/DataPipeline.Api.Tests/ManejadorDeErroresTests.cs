// Fecha: 01/11/2026 - TAREA: 00155 - Linea: 1 - El mensaje que ve el usuario ante un error.
// La diferencia entre "error interno" y "ya existe un registro con ese nombre" es la diferencia
// entre tener que abrir el log y poder arreglarlo solo. Las causas que el usuario PUEDE arreglar se
// nombran; las que no, se mandan al log sin filtrar nada de adentro.
using DataPipeline.Api.Middleware;
using Xunit;

namespace DataPipeline.Api.Tests;

public class ManejadorDeErroresTests
{
    /// <summary>Como llega de verdad: el motivo real esta en la excepcion interna, no en la de arriba.</summary>
    private static Exception ConInterna(string mensajeInterno) =>
        new InvalidOperationException(
            "An error occurred while saving the entity changes. See the inner exception for details.",
            new Exception(mensajeInterno));

    [Fact]
    public void Un_nombre_repetido_se_explica_en_vez_de_mandar_al_log()
    {
        var m = ManejadorDeErrores.Describir(
            ConInterna("SQLite Error 19: 'UNIQUE constraint failed: Origenes.Nombre'."));

        Assert.Contains("Ya existe un registro con ese nombre", m);
        // Nada de internals en la pantalla.
        Assert.DoesNotContain("SQLite", m);
        Assert.DoesNotContain("Origenes.Nombre", m);
    }

    [Fact]
    public void La_base_ocupada_sugiere_reintentar()
    {
        var m = ManejadorDeErrores.Describir(ConInterna("SQLite Error 5: 'database is locked'."));

        Assert.Contains("ocupada", m);
        Assert.Contains("de nuevo", m);
    }

    [Fact]
    public void Un_campo_obligatorio_faltante_se_nombra()
    {
        var m = ManejadorDeErrores.Describir(
            ConInterna("SQLite Error 19: 'NOT NULL constraint failed: Pipelines.Nombre'."));

        Assert.Contains("obligatorio", m);
    }

    [Fact]
    public void Un_error_de_validacion_propio_se_muestra_tal_cual()
    {
        var m = ManejadorDeErrores.Describir(
            new ArgumentException("La tabla destino no puede tener tres partes."));

        Assert.Equal("La tabla destino no puede tener tres partes.", m);
    }

    [Fact]
    public void Un_error_que_no_se_reconoce_manda_al_log_sin_filtrar_nada()
    {
        var m = ManejadorDeErrores.Describir(
            new Exception("Object reference not set to an instance of an object."));

        Assert.Contains("log", m, StringComparison.OrdinalIgnoreCase);
        // El mensaje interno no sale: puede tener rutas, nombres de tablas o datos.
        Assert.DoesNotContain("Object reference", m);
    }

    [Fact]
    public void Busca_la_causa_en_toda_la_cadena_de_internas()
    {
        // Tres niveles: EF envuelve a Sqlite, y arriba de todo hay otra envoltura.
        var ex = new Exception("fallo la operacion",
            new InvalidOperationException("error al guardar",
                new Exception("SQLite Error 19: 'UNIQUE constraint failed: Envios.Nombre'.")));

        Assert.Contains("Ya existe un registro", ManejadorDeErrores.Describir(ex));
    }
}
