namespace CleanArchitecture.Application.Orders.PlaceOrder;

/// <summary>Una línea solicitada al confirmar un pedido.</summary>
/// <param name="ProductId">Producto pedido.</param>
/// <param name="Quantity">Unidades pedidas.</param>
public sealed record PlaceOrderLine(Guid ProductId, int Quantity);

/// <summary>Datos para confirmar un pedido.</summary>
/// <param name="Currency">Moneda del pedido.</param>
/// <param name="Lines">Líneas solicitadas.</param>
public sealed record PlaceOrderCommand(string Currency, IReadOnlyList<PlaceOrderLine> Lines);
