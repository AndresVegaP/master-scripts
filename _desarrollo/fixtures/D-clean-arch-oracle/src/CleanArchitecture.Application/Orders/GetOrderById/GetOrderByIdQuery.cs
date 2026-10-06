namespace CleanArchitecture.Application.Orders.GetOrderById;

/// <summary>Pregunta por un pedido concreto.</summary>
/// <param name="OrderId">Identidad del pedido buscado.</param>
public sealed record GetOrderByIdQuery(Guid OrderId);
