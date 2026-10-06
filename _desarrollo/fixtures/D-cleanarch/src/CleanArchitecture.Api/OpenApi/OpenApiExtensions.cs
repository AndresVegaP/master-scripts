using Microsoft.OpenApi;

namespace CleanArchitecture.Api.OpenApi;

/// <summary>Configura el documento OpenAPI que describe esta API.</summary>
internal static class OpenApiExtensions
{
    // 📘 docs/08-api-http-y-openapi.md
    //
    // OpenAPI es la ESPECIFICACIÓN (un JSON que describe rutas, parámetros,
    // respuestas y esquemas); Swagger UI es solo una interfaz que lo lee y te
    // deja probar la API desde el navegador. Desde .NET 9 el documento lo
    // genera el propio framework: no hace falta Swashbuckle para generarlo,
    // solo para la interfaz visual.
    //
    // ¿3.0 o 3.1? .NET 10 genera 3.1 por defecto. Aquí fijamos 3.0 porque
    // muchísimo tooling (generadores de clientes, pasarelas de API, portales
    // de documentación) todavía no lee 3.1. La diferencia visible está en los
    // nullables: 3.1 usa "type": ["string","null"] y 3.0 usa "nullable": true.

    /// <summary>Registra la generación del documento OpenAPI 3.0.</summary>
    public static IServiceCollection AddOpenApiDocumentation(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;

            // Un "document transformer" puede tocar el documento completo antes
            // de servirlo. Lo usamos para los metadatos generales, que no se
            // pueden deducir de los endpoints.
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Catálogo y pedidos (repo didáctico de DDD + Clean Architecture)",
                    Version = "v1",
                    Description =
                        "API de ejemplo para aprender DDD y Clean Architecture con .NET 10, Dapper y SQLite. "
                        + "Los errores usan el formato Problem Details (RFC 9457) e incluyen un campo errorCode estable.",
                    Contact = new OpenApiContact
                    {
                        Name = "Código fuente y guía de aprendizaje",
                        Url = new Uri("https://github.com/AndresVegaP/clean-architecture-ddd-dotnet"),
                    },
                    License = new OpenApiLicense
                    {
                        Name = "MIT",
                        Url = new Uri("https://opensource.org/licenses/MIT"),
                    },
                };

                // Descripción de cada grupo de operaciones (los tags que usan
                // los endpoints con WithTags).
                document.Tags = new HashSet<OpenApiTag>
                {
                    new() { Name = "Productos", Description = "Catálogo: alta, consulta, cambio de precio y baja." },
                    new() { Name = "Pedidos", Description = "Pedidos: confirmación, consulta y cancelación." },
                };

                return Task.CompletedTask;
            });
        });
}
