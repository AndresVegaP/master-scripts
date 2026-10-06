using CleanArchitecture.Domain.Products;

namespace CleanArchitecture.Application.Products;

/// <summary>Vista de un producto que devuelven los casos de uso.</summary>
/// <param name="Id">Identidad del producto.</param>
/// <param name="Sku">Código de negocio.</param>
/// <param name="Name">Nombre visible.</param>
/// <param name="Price">Importe del precio.</param>
/// <param name="Currency">Código ISO de la moneda.</param>
/// <param name="Stock">Unidades disponibles.</param>
/// <param name="CreatedAt">Momento de creación.</param>
/// <param name="UpdatedAt">Momento de la última modificación, o null.</param>
public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    int Stock,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    // 📘 docs/07-casos-de-uso.md
    //
    // ¿POR QUÉ NO DEVOLVER LA ENTIDAD Product?
    //   1. DESACOPLAMIENTO: si la salida fuera el agregado, cualquier refactor
    //      del dominio cambiaría lo que ven los clientes.
    //   2. APLANADO: el agregado tiene value objects (Price es un Money); aquí
    //      se convierten en datos simples.
    //   3. CONTROL: decides campo a campo qué sale. Si el producto tuviera
    //      costos o márgenes, no se filtrarían por accidente.
    //   4. SIN COMPORTAMIENTO: nadie puede llamar a RemoveStock sobre una
    //      respuesta.
    //
    // Este DTO es el contrato de la capa Application. La Api tiene el SUYO
    // (ProductResponse) porque son cosas distintas: una es interna y la otra es
    // pública. Lo explica docs/08-api-http-y-openapi.md.

    /// <summary>Convierte el agregado en su vista de salida.</summary>
    public static ProductDto FromProduct(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new ProductDto(
            product.Id.Value,
            product.Sku.Value,
            product.Name.Value,
            product.Price.Amount,
            product.Price.Currency.Code,
            product.Stock,
            product.CreatedAt,
            product.UpdatedAt);
    }
}
