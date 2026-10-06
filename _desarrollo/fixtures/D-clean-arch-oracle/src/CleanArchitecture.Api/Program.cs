using System.Diagnostics;
using System.Text.Json.Serialization;
using CleanArchitecture.Api.ErrorHandling;
using CleanArchitecture.Api.OpenApi;
using CleanArchitecture.Api.Orders;
using CleanArchitecture.Api.Products;
using CleanArchitecture.Api.Reports;
using CleanArchitecture.Application;
using CleanArchitecture.Infrastructure;

// ═══════════════════════════════════════════════════════════════════════
// Program.cs — el COMPOSITION ROOT de la aplicación.
//
// Es el ÚNICO lugar donde todas las capas se encuentran y se ensamblan.
// Aquí (y solo aquí) es legal conocer a la vez Application e Infrastructure.
//
// Usa "top-level statements": no hace falta escribir la clase Program ni el
// método Main, el compilador los genera.
// ═══════════════════════════════════════════════════════════════════════

var builder = WebApplication.CreateBuilder(args);

// ─── 1. SERVICIOS ──────────────────────────────────────────────────────

// Cada capa registra lo suyo. Program.cs no sabe QUÉ hay dentro de cada
// AddXxx: solo compone. Si agregas un caso de uso, este archivo no cambia.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Validación de FORMA de las peticiones (.NET 10): revisa los atributos
// [Required], [Range]... de los contratos y responde 400 con el detalle por
// campo. Ojo con la división de responsabilidades:
//   aquí  -> ¿el JSON trae los campos y con valores razonables?
//   dominio -> ¿el dato cumple las reglas del negocio? (formato del SKU,
//              moneda admitida, stock suficiente...)
builder.Services.AddValidation();

// Los números llegan y salen como números. Por defecto, la configuración web
// de System.Text.Json acepta también números entre comillas, lo que ensucia el
// esquema OpenAPI con "number o string" en cada importe.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

// Errores con formato Problem Details (RFC 9457) en toda la aplicación,
// incluidos los que genera el propio framework (404 de ruta, 405, 415) y los
// de validación. CustomizeProblemDetails los completa para que TODOS los
// errores de la API se vean iguales: mismo título en español, instance y
// traceId, y un errorCode cuando se puede deducir.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var problemDetails = context.ProblemDetails;
    var request = context.HttpContext.Request;

    problemDetails.Instance ??= $"{request.Method} {request.Path}";
    problemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);

    // La validación de .NET devuelve el detalle campo por campo. Ojo: esos
    // errores NO son una extensión, son una propiedad del tipo
    // HttpValidationProblemDetails, así que se detectan por el TIPO.
    // Le ponemos código propio para que el cliente lo trate como los demás.
    if (problemDetails is HttpValidationProblemDetails)
    {
        problemDetails.Extensions.TryAdd("errorCode", "Request.ValidationFailed");
        problemDetails.Title = "La petición tiene errores de validación";
        return;
    }

    problemDetails.Title = problemDetails.Status switch
    {
        StatusCodes.Status400BadRequest => "Petición inválida",
        StatusCodes.Status404NotFound => "Recurso no encontrado",
        StatusCodes.Status405MethodNotAllowed => "Método no permitido en esta ruta",
        StatusCodes.Status415UnsupportedMediaType => "Tipo de contenido no soportado",
        _ => problemDetails.Title,
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddOpenApiDocumentation();

var app = builder.Build();

// ─── 2. PIPELINE HTTP (el orden IMPORTA) ───────────────────────────────

// Primero el manejador de excepciones: así envuelve todo lo que venga después.
app.UseExceptionHandler();

// Da cuerpo Problem Details a las respuestas de error SIN cuerpo que genera el
// framework (por ejemplo, un 404 porque la ruta no existe o el {id:guid} no
// es un Guid válido).
app.UseStatusCodePages();

// La especificación y la interfaz visual solo en desarrollo: en producción, el
// documento de la API suele publicarse aparte y con control de acceso.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // sirve /openapi/v1.json

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "API v1");
        options.RoutePrefix = "swagger";
    });

    // Comodidad: entrar a la raíz lleva a Swagger UI.
    app.MapGet("/", () => Results.Redirect("/swagger"))
       .ExcludeFromDescription();
}

// Los endpoints, agrupados por recurso en sus propios archivos.
app.MapProductEndpoints();
app.MapOrderEndpoints();

// Los reportes cuelgan de un grupo "/api" creado AQUÍ y pasado a la extensión:
// el prefijo común se declara una sola vez y ReportEndpoints solo agrega su
// parte ("/reports").
var api = app.MapGroup("/api");
api.MapReportEndpoints();

// ─── 3. BASE DE DATOS ──────────────────────────────────────────────────

// Aplica las migraciones pendientes al arrancar. La Api no sabe que por dentro
// hay un migrador con SQL: solo pide que la base quede lista.
await app.Services.MigrateDatabaseAsync();

// ─── 4. ARRANCAR ───────────────────────────────────────────────────────

await app.RunAsync();

// Hace visible la clase Program (que genera el compilador) para los tests de
// integración, que arrancan la API en memoria con WebApplicationFactory<Program>.
public partial class Program;
