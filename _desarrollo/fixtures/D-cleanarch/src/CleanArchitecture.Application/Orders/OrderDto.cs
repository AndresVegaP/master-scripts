using CleanArchitecture.Domain.Orders;

namespace CleanArchitecture.Application.Orders;

/// <summary>Una línea dentro de la vista de un pedido.</summary>
/// <param name="ProductId">Producto pedido.</param>
/// <param name="UnitPrice">Precio unitario acordado.</param>
/// <param name="Quantity">Unidades pedidas.</param>
/// <param name="Subtotal">Importe de la línea.</param>
public sealed record OrderLineDto(Guid ProductId, decimal UnitPrice, int Quantity, decimal Subtotal);

/// <summary>Vista de un pedido que devuelven los casos de uso.</summary>
/// <param name="Id">Identidad del pedido.</param>
/// <param name="Status">Estado actual.</param>
/// <param name="Currency">Código ISO de la moneda.</param>
/// <param name="Total">Importe total.</param>
/// <param name="CreatedAt">Momento de creación.</param>
/// <param name="PlacedAt">Momento de confirmación, o null.</param>
/// <param name="CancelledAt">Momento de cancelación, o null.</param>
/// <param name="Lines">Líneas del pedido.</param>
public sealed record OrderDto(
    Guid Id,
    string Status,
    string Currency,
    decimal Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PlacedAt,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<OrderLineDto> Lines)
{
    /// <summary>Convierte el agregado en su vista de salida.</summary>
    public static OrderDto FromOrder(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderDto(
            order.Id.Value,
            order.Status.ToString(),
            order.Currency.Code,
            order.Total.Amount,
            order.CreatedAt,
            order.PlacedAt,
            order.CancelledAt,
            [
                .. order.Lines.Select(line => new OrderLineDto(
                    line.ProductId.Value,
                    line.UnitPrice.Amount,
                    line.Quantity,
                    line.Subtotal.Amount)),
            ]);
    }
}
