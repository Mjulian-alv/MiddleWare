// Fecha: 01/11/2026 - TAREA: 00155 - Linea: 1 - Log a archivo.
// Lo que mas importa de un logger no es que escriba: es que NO tire. Si el log revienta, se lleva
// puesta la aplicacion que se estaba tratando de diagnosticar, que es el peor momento posible.
using DataPipeline.Core.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DataPipeline.Tests;

public class ArchivoLoggerTests : IDisposable
{
    private readonly string _carpeta =
        Path.Combine(Path.GetTempPath(), $"dplog_{Guid.NewGuid():N}");

    private ArchivoLoggerProvider Proveedor(Action<ArchivoLoggerOptions>? cfg = null)
    {
        var op = new ArchivoLoggerOptions { Carpeta = _carpeta, Prefijo = "prueba" };
        cfg?.Invoke(op);
        return new ArchivoLoggerProvider(op);
    }

    private string[] Lineas()
    {
        var archivo = Directory.GetFiles(_carpeta, "prueba-*.log").Single();
        return File.ReadAllLines(archivo);
    }

    [Fact]
    public void Escribe_el_mensaje_con_fecha_nivel_y_categoria()
    {
        using var prov = Proveedor();
        prov.CreateLogger("MiCategoria").LogWarning("algo raro paso");

        var l = Lineas().Single();
        Assert.Contains("[WRN]", l);
        Assert.Contains("MiCategoria", l);
        Assert.Contains("algo raro paso", l);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} ", l);
    }

    [Fact]
    public void La_excepcion_va_completa_con_su_stack()
    {
        using var prov = Proveedor();
        try
        {
            throw new InvalidOperationException("la causa de verdad");
        }
        catch (Exception ex)
        {
            prov.CreateLogger("X").LogError(ex, "fallo al guardar");
        }

        var texto = string.Join("\n", Lineas());
        Assert.Contains("fallo al guardar", texto);
        Assert.Contains("la causa de verdad", texto);          // el mensaje de la excepcion
        Assert.Contains("InvalidOperationException", texto);   // y el tipo
        Assert.Contains("ArchivoLoggerTests", texto);          // y el stack
    }

    [Fact]
    public void Respeta_el_nivel_minimo()
    {
        using var prov = Proveedor(o => o.Minimo = LogLevel.Warning);
        var log = prov.CreateLogger("X");

        log.LogInformation("esto no va");
        log.LogError("esto si");

        var l = Lineas().Single();
        Assert.Contains("esto si", l);
    }

    [Fact]
    public void Rota_cuando_el_archivo_se_pasa_del_tamano()
    {
        using var prov = Proveedor(o => { o.MaxArchivoMB = 1; o.Minimo = LogLevel.Trace; });
        var log = prov.CreateLogger("X");

        var relleno = new string('x', 200_000);
        for (int i = 0; i < 12; i++) log.LogInformation("{Relleno}", relleno);

        var archivos = Directory.GetFiles(_carpeta, "prueba-*.log");
        Assert.True(archivos.Length > 1, "tendria que haber abierto un archivo nuevo");
    }

    // El caso que realmente importa: si el log no puede escribir, la aplicacion sigue.
    [Fact]
    public void Si_no_puede_escribir_no_tira()
    {
        var rutaImposible = Path.Combine(Path.GetTempPath(), $"dplog_{Guid.NewGuid():N}", "archivo.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(rutaImposible)!);
        File.WriteAllText(rutaImposible, "soy un archivo, no una carpeta");

        // Se le pide usar como CARPETA algo que es un archivo: crear el directorio va a fallar.
        var prov = new ArchivoLoggerProvider(new ArchivoLoggerOptions
        {
            Carpeta = rutaImposible,
            Prefijo = "x"
        });

        var ex = Record.Exception(() => prov.CreateLogger("X").LogError("un error cualquiera"));

        Assert.Null(ex);
        prov.Dispose();
    }

    [Fact]
    public void Borra_los_archivos_mas_viejos_que_el_limite()
    {
        Directory.CreateDirectory(_carpeta);
        var viejo = Path.Combine(_carpeta, "prueba-20200101.log");
        File.WriteAllText(viejo, "de otra epoca");
        File.SetLastWriteTime(viejo, DateTime.Now.AddDays(-90));

        var reciente = Path.Combine(_carpeta, "prueba-20261030.log");
        File.WriteAllText(reciente, "de ayer");

        using var prov = Proveedor(o => o.DiasAConservar = 30);

        Assert.False(File.Exists(viejo));
        Assert.True(File.Exists(reciente));
    }

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, recursive: true); } catch { /* da igual */ }
    }
}
