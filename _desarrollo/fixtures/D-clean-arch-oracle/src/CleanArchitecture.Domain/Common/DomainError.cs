namespace CleanArchitecture.Domain.Common;

/// <summary>Naturaleza de un error de dominio. Determina cómo lo traduce la capa Api.</summary>
public enum DomainErrorKind
{
    /// <summary>
    /// El dato o la operación son inválidos en sí mismos (un SKU mal formado,
    /// un importe negativo). La Api responde 422 Unprocessable Content.
    /// </summary>
    Invariant,

    /// <summary>
    /// La operación choca con el ESTADO ACTUAL del agregado (no hay stock
    /// suficiente, el pedido ya está cancelado). La Api responde 409 Conflict.
    /// </summary>
    Conflict,
}

/// <summary>Un error de negocio con nombre propio: código estable, mensaje y naturaleza.</summary>
/// <param name="Code">Identificador estable con formato "Concepto.Regla" (por ejemplo, "Product.InsufficientStock").</param>
/// <param name="Message">Explicación para personas. Puede cambiar sin romper a nadie.</param>
/// <param name="Kind">Naturaleza del error, que decide el código HTTP.</param>
public sealed record DomainError(string Code, string Message, DomainErrorKind Kind);
