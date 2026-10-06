namespace CleanArchitecture.Domain.Products;

/// <summary>Identidad de un producto.</summary>
/// <param name="Value">Valor subyacente del identificador.</param>
public readonly record struct ProductId(Guid Value)
{
    // 📘 docs/04-entidades.md
    //
    // ¿Por qué un tipo propio y no un Guid pelado? Porque un Guid acepta
    // cualquier Guid: nada impide pasarle a un método el Id de un pedido donde
    // esperaba el de un producto, y ese bug solo aparece en ejecución.
    // Con ProductId, el compilador lo rechaza y las firmas se leen solas:
    //
    //     Task<Product?> GetByIdAsync(ProductId id)   // imposible confundirse
    //
    // Es un value object: sin identidad propia, inmutable y comparado por
    // valor. Al no tener reglas, un record struct es la herramienta perfecta
    // (cero asignaciones en memoria y todo el código lo genera el compilador).

    /// <summary>Genera una identidad nueva, ordenable en el tiempo (UUID versión 7).</summary>
    public static ProductId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
