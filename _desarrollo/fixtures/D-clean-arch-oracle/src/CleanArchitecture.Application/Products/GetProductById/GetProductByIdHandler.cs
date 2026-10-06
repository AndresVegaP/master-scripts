using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Domain.Products;

namespace CleanArchitecture.Application.Products.GetProductById;

/// <summary>Caso de uso: obtener un producto por su Id.</summary>
public sealed class GetProductByIdHandler(IProductRepository productRepository)
{
    // Este caso de uso lee A TRAVÉS DEL AGREGADO, a diferencia de ListProducts,
    // que usa el puerto de lectura (IProductQueries). Están así a propósito,
    // para que compares los dos caminos de CQRS en el mismo repositorio:
    //
    //   - Por el agregado: menos código, una sola forma de leer, y el objeto
    //     que obtienes ya trae sus reglas (útil si después vas a modificarlo).
    //   - Por el puerto de lectura: SQL directo al DTO, sin construir value
    //     objects que se descartan. Gana cuando el listado es grande o la
    //     consulta es de pantalla.

    /// <summary>Ejecuta el caso de uso.</summary>
    public async Task<Result<ProductDto>> ExecuteAsync(
        GetProductByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // El repositorio devuelve Product? (nullable): el compilador OBLIGA a
        // considerar el caso "no existe" antes de usar el objeto.
        var product = await productRepository.GetByIdAsync(new ProductId(query.ProductId), cancellationToken);

        if (product is null)
        {
            // "No encontrado" es un resultado normal de una búsqueda, no un
            // error del programa: viaja como Result y la Api lo traduce a 404.
            return Result.Failure<ProductDto>(ProductUseCaseErrors.NotFound(query.ProductId));
        }

        return ProductDto.FromProduct(product);
    }
}
