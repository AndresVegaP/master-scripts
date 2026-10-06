using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Domain.Products;

namespace CleanArchitecture.Application.Orders;

/// <summary>Fallos esperados de los casos de uso de pedidos.</summary>
public static class OrderUseCaseErrors
{
    /// <summary>El pedido no existe.</summary>
    public static Error NotFound(Guid orderId) =>
        Error.NotFound("Order.NotFound", $"No existe ningún pedido con el Id {orderId}.");

    /// <summary>Se pidió un producto que no está en el catálogo.</summary>
    public static Error ProductNotFound(Guid productId) =>
        Error.NotFound("Order.ProductNotFound", $"No existe ningún producto con el Id {productId}.");

    /// <summary>No hay stock suficiente para una o varias líneas.</summary>
    public static Error InsufficientStock(IReadOnlyList<StockShortage> shortages)
    {
        ArgumentNullException.ThrowIfNull(shortages);

        // Un solo mensaje con TODOS los productos sin stock: quien usa la API
        // arregla su pedido de una vez, en lugar de reintentar y descubrir uno
        // nuevo cada vez. Para eso existe el servicio de dominio StockAvailability.
        var detail = string.Join(
            "; ",
            shortages.Select(shortage =>
                $"{shortage.Sku}: {shortage.Available} disponibles, {shortage.Requested} solicitadas"));

        return Error.Conflict("Order.InsufficientStock", $"No hay stock suficiente ({detail}).");
    }

    /// <summary>Otra operación modificó el pedido o alguno de sus productos mientras tanto.</summary>
    public static Error ConcurrencyConflict(Guid orderId) =>
        Error.Conflict(
            "Order.ConcurrencyConflict",
            $"El pedido {orderId} o alguno de sus productos cambió mientras se procesaba la operación. Vuelve a intentarlo.");
}
