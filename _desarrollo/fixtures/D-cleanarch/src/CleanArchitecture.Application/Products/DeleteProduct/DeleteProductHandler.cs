using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Domain.Orders;
using CleanArchitecture.Domain.Products;

namespace CleanArchitecture.Application.Products.DeleteProduct;

/// <summary>Caso de uso: eliminar un producto del catálogo.</summary>
public sealed class DeleteProductHandler(IProductRepository productRepository, IOrderRepository orderRepository)
{
    // REGLA ENTRE AGREGADOS, y por eso vive aquí y no en Product:
    // "no se elimina un producto que aparece en pedidos".
    //
    // Product no puede comprobarlo (no conoce los pedidos, ni debe), y Order
    // tampoco (la operación no es suya). Es una regla que necesita mirar DOS
    // agregados, así que la coordina el caso de uso. Esa es justamente la
    // diferencia entre orquestar (aquí) y decidir (dentro del agregado).
    //
    // En un sistema real probablemente se preferiría una BAJA LÓGICA
    // (marcar el producto como descatalogado y dejar de mostrarlo) antes que
    // borrar la fila. Está propuesto como ejercicio en docs/12-proyecto-final.md.

    /// <summary>Ejecuta el caso de uso.</summary>
    public async Task<Result> ExecuteAsync(DeleteProductCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var productId = new ProductId(command.ProductId);

        if (await orderRepository.ExistsWithProductAsync(productId, cancellationToken))
        {
            return Result.Failure(ProductUseCaseErrors.UsedInOrders(command.ProductId));
        }

        // DeleteAsync devuelve si borró algo: así sabemos si existía con UNA
        // sola ida a la base de datos, en vez de consultar y después borrar.
        var deleted = await productRepository.DeleteAsync(productId, cancellationToken);

        return deleted
            ? Result.Success()
            : Result.Failure(ProductUseCaseErrors.NotFound(command.ProductId));
    }
}
