using CleanArchitecture.Application.Abstractions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CleanArchitecture.Api.ErrorHandling;

/// <summary>Traduce los fallos de la capa Application al idioma de HTTP.</summary>
internal static class ResultExtensions
{
    // 📘 docs/08-api-http-y-openapi.md
    //
    // Esta clase diminuta es la razón por la que Application puede ignorar que
    // HTTP existe: Application dice "NotFound" y aquí, en la única capa que
    // sabe de HTTP, se decide que eso significa 404.
    //
    // Cada capa habla su idioma; las fronteras traducen.

    /// <summary>Convierte un <see cref="Error"/> en una respuesta Problem Details.</summary>
    public static ProblemHttpResult ToProblem(this Error error, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);

        var (statusCode, title) = error.Type switch
        {
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Recurso no encontrado"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflicto con el estado actual"),

            // El descarte "_" cubre valores futuros del enum: si alguien agrega
            // una categoría y olvida mapearla, mejor un 500 visible que un
            // comportamiento silencioso e indefinido.
            _ => (StatusCodes.Status500InternalServerError, "Error interno"),
        };

        return TypedResults.Problem(ApiProblems.Create(httpContext, statusCode, title, error.Message, error.Code));
    }
}
