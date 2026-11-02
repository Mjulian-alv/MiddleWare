// Fecha: 01/11/2026 - TAREA: 00155 - Linea: 1 - Lectura del error que manda la API.
// Esto existe por un sintoma concreto: guardar un pipeline fallaba y la pantalla decia "no se pudo
// guardar (revisa la API o el nombre duplicado)" — dos causas posibles entre muchas, ninguna
// confirmada. El motivo real venia en la respuesta y se tiraba. Lo que se prueba aca es que, venga
// como venga el error (formato propio, ProblemDetails, texto pelado o nada), llegue algo accionable.
using System.Net;
using System.Text;
using DataPipeline.Admin.Services;
using Xunit;

namespace DataPipeline.Tests;

public class ErroresApiTests
{
    private static HttpResponseMessage Resp(HttpStatusCode codigo, string cuerpo, string tipo = "application/json") =>
        new(codigo) { Content = new StringContent(cuerpo, Encoding.UTF8, tipo) };

    [Fact]
    public async Task Una_respuesta_buena_no_es_error()
    {
        var r = await ErroresApi.DeRespuestaAsync(Resp(HttpStatusCode.OK, "{}"));

        Assert.True(r.Ok);
        Assert.Null(r.Error);
    }

    [Fact]
    public async Task Lee_el_formato_propio_de_la_API_con_su_traceId()
    {
        var r = await ErroresApi.DeRespuestaAsync(Resp(HttpStatusCode.InternalServerError,
            """{"error":"El nombre 'ventas' ya existe.","traceId":"0HNA1B2C3D4E5"}"""));

        Assert.False(r.Ok);
        Assert.Equal("El nombre 'ventas' ya existe.", r.Error);
        Assert.Equal("0HNA1B2C3D4E5", r.TraceId);
        // El traceId tiene que verse: es lo que permite encontrar ese error en el log del servidor.
        Assert.Contains("0HNA1B2C3D4E5", r.Mostrar());
    }

    [Fact]
    public async Task Lee_la_validacion_del_modelo_de_ASPNET_que_viene_en_errors()
    {
        // Es lo que devuelve [ApiController] cuando el modelo no valida: el detalle util esta en
        // "errors", no en "title" (que solo dice "One or more validation errors occurred.").
        var r = await ErroresApi.DeRespuestaAsync(Resp(HttpStatusCode.BadRequest, """
            {"type":"https://tools.ietf.org/html/rfc9110","title":"One or more validation errors occurred.",
             "status":400,
             "errors":{"TablaDestino":["The TablaDestino field is required."],
                       "Nombre":["Largo maximo 100."]}}
            """));

        Assert.False(r.Ok);
        Assert.Contains("TablaDestino", r.Error);
        Assert.Contains("required", r.Error);
        Assert.Contains("Nombre", r.Error);
        Assert.DoesNotContain("One or more validation errors", r.Error);
    }

    [Fact]
    public async Task Usa_detail_cuando_no_hay_errors()
    {
        var r = await ErroresApi.DeRespuestaAsync(Resp(HttpStatusCode.Conflict,
            """{"title":"Conflict","detail":"Ya existe un pipeline con ese nombre."}"""));

        Assert.Equal("Ya existe un pipeline con ese nombre.", r.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "credenciales")]
    [InlineData(HttpStatusCode.Forbidden, "permiso")]
    [InlineData(HttpStatusCode.NotFound, "no encontro")]
    [InlineData(HttpStatusCode.Conflict, "nombre ya existe")]
    public async Task Sin_cuerpo_util_describe_el_codigo_en_vez_de_inventar_una_causa(
        HttpStatusCode codigo, string esperado)
    {
        var r = await ErroresApi.DeRespuestaAsync(Resp(codigo, ""));

        Assert.False(r.Ok);
        Assert.Contains(esperado, r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_cuerpo_que_no_es_json_se_muestra_igual()
    {
        var r = await ErroresApi.DeRespuestaAsync(
            Resp(HttpStatusCode.InternalServerError, "Server Error: la base esta bloqueada", "text/plain"));

        Assert.Contains("la base esta bloqueada", r.Error);
    }

    [Fact]
    public async Task Un_cuerpo_enorme_se_recorta_para_que_entre_en_pantalla()
    {
        var r = await ErroresApi.DeRespuestaAsync(
            Resp(HttpStatusCode.InternalServerError, new string('x', 5000), "text/plain"));

        Assert.True(r.Error!.Length <= 510, $"quedo en {r.Error.Length} caracteres");
        Assert.EndsWith("...", r.Error);
    }

    [Fact]
    public async Task Un_500_sin_cuerpo_manda_a_buscar_el_detalle_al_log()
    {
        var r = await ErroresApi.DeRespuestaAsync(Resp(HttpStatusCode.InternalServerError, ""));

        Assert.Contains("log", r.Error, StringComparison.OrdinalIgnoreCase);
    }
}
