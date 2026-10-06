namespace CleanArchitecture.Application.Abstractions;

/// <summary>Categoría de un fallo de caso de uso. La capa Api la traduce a un código HTTP.</summary>
public enum ErrorType
{
    /// <summary>Lo que se pidió no existe. La Api responde 404.</summary>
    NotFound,

    /// <summary>Choca con algo que ya está guardado o cambió mientras tanto. La Api responde 409.</summary>
    Conflict,
}

/// <summary>Un fallo esperado de un caso de uso, con código estable y mensaje.</summary>
/// <param name="Code">Identificador estable, con formato "Concepto.Motivo".</param>
/// <param name="Message">Explicación para personas.</param>
/// <param name="Type">Categoría, para que la Api elija el código HTTP.</param>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    // 📘 docs/07-casos-de-uso.md
    //
    // ¿Por qué no un simple string? Porque un string no se puede procesar:
    //   - Code: identificador estable ("Product.NotFound") contra el que los
    //     clientes pueden programar. El texto cambia; el código, no.
    //   - Message: para personas (logs, respuestas, depuración).
    //   - Type: la categoría, para mapear a HTTP SIN que Application sepa de HTTP.
    //
    // Ojo con la diferencia respecto a DomainError (capa Domain): aquel
    // representa una REGLA DE NEGOCIO rota; este, un resultado del caso de uso
    // que solo se puede saber consultando el almacén.

    /// <summary>Crea un error de "no encontrado" (404).</summary>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    /// <summary>Crea un error de conflicto (409).</summary>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
}
