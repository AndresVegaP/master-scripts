using System.ComponentModel.DataAnnotations;
using CleanArchitecture.Application.Orders;

namespace CleanArchitecture.Api.Orders;

/// <summary>Una línea solicitada dentro de un pedido.</summary>
/// <param name="ProductId">Identificador del producto.</param>
/// <param name="Quantity">Unidades pedidas (entre 1 y 1000).</param>
public sealed record PlaceOrderLineRequest(
    [Required] Guid? ProductId,
    [Range(1, 1000)] int Quantity);

/// <summary>Cuerpo de la petición para confirmar un pedido.</summary>
/// <param name="Currency">Moneda del pedido. Debe coincidir con la de los productos pedidos.</param>
/// <param name="Lines">Líneas del pedido. Al menos una.</param>
public sealed record PlaceOrderRequest(
    [Required] string? Currency,
    [Required][MinLength(1)] IReadOnlyList<PlaceOrderLineRequest>? Lines);

/// <summary>Línea de un pedido en la respuesta.</summary>
/// <param name="ProductId">Producto pedido.</param>
/// <param name="UnitPrice">Precio unitario acordado al confirmar el pedido.</param>
/// <param name="Quantity">Unidades pedidas.</param>
/// <param name="Subtotal">Importe de la línea.</param>
public sealed record OrderLineResponse(Guid ProductId, decimal UnitPrice, int Quantity, decimal Subtotal);

/// <summary>Representación pública de un pedido.</summary>
/// <param name="Id">Identificador del pedido.</param>
/// <param name="Status">Estado: Draft, Placed o Cancelled.</param>
/// <param name="Currency">Moneda del pedido.</param>
/// <param name="Total">Importe total.</param>
/// <param name="CreatedAt">Fecha y hora de creación.</param>
/// <param name="PlacedAt">Fecha y hora de confirmación, o null.</param>
/// <param name="CancelledAt">Fecha y hora de cancelación, o null.</param>
/// <param name="Lines">Líneas del pedido.</param>
public sealed record OrderResponse(
    Guid Id,
    string Status,
    string Currency,
    decimal Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PlacedAt,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<OrderLineResponse> Lines)
{
    /// <summary>Convierte la salida del caso de uso en la respuesta pública.</summary>
    public static OrderResponse FromDto(OrderDto order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderResponse(
            order.Id,
            order.Status,
            order.Currency,
            order.Total,
            order.CreatedAt,
            order.PlacedAt,
            order.CancelledAt,
            [.. order.Lines.Select(line => new OrderLineResponse(line.ProductId, line.UnitPrice, line.Quantity, line.Subtotal))]);
    }
}
