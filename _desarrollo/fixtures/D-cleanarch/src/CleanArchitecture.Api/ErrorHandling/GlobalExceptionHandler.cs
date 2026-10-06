using CleanArchitecture.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace CleanArchitecture.Api.ErrorHandling;

/// <summary>Convierte cualquier excepción que escape de un endpoint en una respuesta Problem Details.</summary>
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    // 📘 docs/08-api-http-y-openapi.md
    //
    // Implementa IExceptionHandler (.NET 8+), que ASP.NET Core invoca cuando
    // una excepción escapa de un endpoint. Gracias a él:
    //   - no hay try/catch repartidos por los endpoints;
    //   - todas las respuestas de error tienen la misma forma;
    //   - la traducción "excepción -> código HTTP" vive en UN solo lugar.
    //
    // LA TABLA DE TRADUCCIÓN:
    //   DomainException (Invariant)  -> 422 el contenido es válido como JSON,
    //                                   pero rompe una regla de negocio.
    //   DomainException (Conflict)   -> 409 choca con el estado actual.
    //   BadHttpRequestException      -> el estado que ella misma trae (400 si el
    //                                   JSON viene roto, 413 si el cuerpo es
    //                                   enorme...). Es culpa del cliente.
    //   CorruptedSnapshotException   -> 500 un dato guardado ya no cumple las
    //                                   reglas: la culpa es NUESTRA.
    //   Cualquier otra               -> 500.
    //
    // Detalle importante de .NET 10: cuando un IExceptionHandler devuelve true,
    // el framework ya no registra la excepción por su cuenta. Si este handler no
    // escribiera logs, los errores desaparecerían en silencio.

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, errorCode) = exception switch
        {
            DomainException domainException => (
                domainException.Kind == DomainErrorKind.Conflict
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status422UnprocessableEntity,
                domainException.Kind == DomainErrorKind.Conflict
                    ? "Conflicto con el estado actual"
                    : "Regla de negocio violada",
                domainException.Code),

            // La lanza ASP.NET Core cuando no puede LEER la petición: JSON
            // malformado, un número donde va un texto, un cuerpo demasiado
            // grande... Trae su propio código de estado y hay que respetarlo.
            //
            // Nota para cuando pruebes esto: en Development, minimal APIs lanza
            // esta excepción; en Producción responde 400 directamente sin pasar
            // por aquí (lo controla RouteHandlerOptions.ThrowOnBadRequest).
            BadHttpRequestException badRequest => (
                badRequest.StatusCode,
                "Petición malformada",
                (string?)"Request.Malformed"),

            CorruptedSnapshotException => (
                StatusCodes.Status500InternalServerError,
                "Error interno del servidor",
                (string?)"Persistence.CorruptedData"),

            _ => (StatusCodes.Status500InternalServerError, "Error interno del servidor", (string?)null),
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            // Culpa nuestra: esto SÍ es un incidente que hay que investigar.
            logger.LogError(exception, "Excepción no controlada procesando {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            // Culpa del cliente: útil para detectar integraciones rotas, pero no
            // debe despertar a nadie de madrugada.
            logger.LogWarning(
                "Petición rechazada en {Method} {Path} con {StatusCode}: {Message}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                statusCode,
                exception.Message);
        }

        // SEGURIDAD: en un 500 NO exponemos el mensaje de la excepción, porque
        // podría filtrar rutas, SQL o versiones. El detalle queda en los logs y
        // el cliente recibe algo genérico junto al traceId para poder reportarlo.
        var detail = statusCode >= StatusCodes.Status500InternalServerError
            ? "Ocurrió un error inesperado. Si el problema persiste, reporta el traceId de esta respuesta."
            : exception.Message;

        var problemDetails = ApiProblems.Create(httpContext, statusCode, title, detail, errorCode);

        // Reusamos exactamente el mismo camino de escritura que los errores de
        // negocio, para que el cliente reciba SIEMPRE la misma forma de error.
        await TypedResults.Problem(problemDetails).ExecuteAsync(httpContext);

        return true; // excepción manejada
    }
}
