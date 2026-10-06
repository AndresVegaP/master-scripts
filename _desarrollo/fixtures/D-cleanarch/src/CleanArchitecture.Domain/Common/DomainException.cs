namespace CleanArchitecture.Domain.Common;

/// <summary>Se lanza cuando una operación rompería una regla de negocio.</summary>
public sealed class DomainException : Exception
{
    // 📘 docs/adr/0003-estrategia-de-errores.md
    //
    // En este repo un fallo se expresa de DOS formas, con un criterio claro:
    //
    //   1. DomainException → el DOMINIO, mirando solo su propio estado, se
    //      niega: precio negativo, stock insuficiente, cancelar un pedido ya
    //      cancelado. La Api responde 422 (invariante) o 409 (conflicto).
    //
    //   2. Result.Failure (capa Application) → el caso de uso necesita
    //      CONSULTAR EL ALMACÉN para saberlo: no existe, ya existe ese SKU,
    //      alguien lo modificó antes que tú. La Api responde 404 o 409.
    //
    // La regla para elegir: ¿puede decidirlo el agregado solo, con lo que tiene
    // en memoria? → excepción de dominio. ¿Hace falta ir a la base de datos?
    // → Result.

    public DomainException(DomainError error)
        : base(error?.Message)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    /// <summary>Error de negocio que provocó la excepción.</summary>
    public DomainError Error { get; }

    /// <summary>Código estable del error, para que los clientes programen contra él.</summary>
    public string Code => Error.Code;

    /// <summary>Naturaleza del error (invariante o conflicto con el estado actual).</summary>
    public DomainErrorKind Kind => Error.Kind;
}
