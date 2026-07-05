using System.Net.Http.Headers;
using System.Text;
using DataPipeline.Admin.Components;
using DataPipeline.Admin.Services;
using Syncfusion.Blazor;

var builder = WebApplication.CreateBuilder(args);

// Razor Components con renderizado interactivo en servidor.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Syncfusion. La licencia se registra desde config "SyncfusionLicenseKey" (o variable de entorno);
// NO se hardcodea en el repo. Sin clave, Syncfusion muestra un banner de trial pero funciona.
var syncfusionKey = builder.Configuration["SyncfusionLicenseKey"] ?? "Ngo9BigBOggjGyl/VkV+XU9AclRDX3xKf0x/TGpQb19xflBPallYVBYiSV9jS3hTcERjWH1ddXRXQ2VcU091XA==00";
if (!string.IsNullOrWhiteSpace(syncfusionKey))
    Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(syncfusionKey);
builder.Services.AddSyncfusionBlazor();

// Auth local del panel.
builder.Services.AddSingleton<AdminAuthService>();
builder.Services.AddScoped<AdminSessionState>();

// ── HttpClient hacia DataPipeline.Api ────────────────────────────────────────
// El Admin habla con la API por HTTP (nunca toca la BD). Las rutas de gestion exigen rol admin,
// asi que cada cliente lleva Authorization: Basic admin:**** desde config.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7002";
var apiUser = builder.Configuration["ApiAuth:Username"] ?? "admin";
var apiPass = builder.Configuration["ApiAuth:Password"] ?? "admin123";
var apiTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("ApiHttpTimeoutSeconds", 200));
var basicHeader = new AuthenticationHeaderValue(
    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiUser}:{apiPass}")));

void ConfigurarApi(HttpClient client)
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = apiTimeout;
    client.DefaultRequestHeaders.Authorization = basicHeader;
}

// Si la API usa un certificado self-signed (tipico en intranet con el cert de desarrollo),
// aceptarlo en las llamadas internas. Opt-in por "ApiAcceptAnyServerCertificate" (default: validar).
var aceptarCertApi = builder.Configuration.GetValue("ApiAcceptAnyServerCertificate", false);
HttpMessageHandler HandlerApi()
{
    var handler = new HttpClientHandler();
    if (aceptarCertApi)
        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    return handler;
}
IHttpClientBuilder ApiClient<T>() where T : class =>
    builder.Services.AddHttpClient<T>(ConfigurarApi).ConfigurePrimaryHttpMessageHandler(HandlerApi);

ApiClient<PipelinesAdminService>();
ApiClient<RunsAdminService>();
ApiClient<DataSourcesAdminService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
