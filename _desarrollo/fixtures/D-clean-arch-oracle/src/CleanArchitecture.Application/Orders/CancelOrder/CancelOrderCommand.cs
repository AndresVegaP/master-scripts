namespace CleanArchitecture.Application.Orders.CancelOrder;

/// <summary>Datos para cancelar un pedido.</summary>
/// <param name="OrderId">Pedido a cancelar.</param>
public sealed record CancelOrderCommand(Guid OrderId);
