using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitecture.Api.ErrorHandling;

/// <summary>Construye las respuestas de error de la API, todas con la misma forma.</summary>
internal static class ApiProblems
{
    // 📘 docs/08-api-http-y-openapi.md
    //
    // PROBLEM DETAILS (RFC 9457, que reemplazó al RFC 7807 en 2023) es el
    // formato estándar para devolver errores en una API HTTP:
    //
    //     {
    //       "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
    //       "title": "Conflicto con el estado actual",
    //       "status": 409,
    //       "detail": "Ya existe un producto con el SKU TEC-0001.",
    //       "instance": "POST /api/products",
    //       "traceId": "00-8f3a...-01",
    //       "errorCode": "Product.SkuAlreadyExists"
    //     }
    //
    // Los cinco primeros campos son del estándar; traceId y errorCode son
    // EXTENSIONES nuestras (el estándar las permite):
    //   - traceId: para cruzar la respuesta con los logs del servidor.
    //   - errorCode: código estable contra el que los clientes pueden programar,
    //     en lugar de leer el texto de detail (que puede cambiar).
    //
    // Toda la API pasa por esta clase (los errores de negocio y las excepciones)
    // para que un cliente no tenga que manejar dos formatos distintos.

    /// <summary>Crea el cuerpo del error con todos los campos ya rellenos.</summary>
    public static ProblemDetails Create(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail,
        string? errorCode = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = TypeForStatusCode(statusCode),
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
        };

        // Activity.Current es el identificador de la traza distribuida que
        // .NET mantiene por petición; si no hay, usamos el de la conexión.
        problemDetails.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (errorCode is not null)
        {
            problemDetails.Extensions["errorCode"] = errorCode;
        }

        return problemDetails;
    }

    /// <summary>
    /// El campo "type" identifica el TIPO de problema. Aquí apuntamos a la
    /// sección del estándar HTTP que define cada código; una API madura suele
    /// apuntar a su propia página de catálogo de errores.
    /// </summary>
    private static string TypeForStatusCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        StatusCodes.Status422UnprocessableEntity => "https://tools.ietf.org/html/rfc9110#section-15.5.21",
        _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
    };
}
