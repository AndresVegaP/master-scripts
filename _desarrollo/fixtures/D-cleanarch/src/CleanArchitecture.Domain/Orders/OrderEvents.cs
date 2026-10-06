using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.Domain.Orders;

/// <summary>Un pedido fue confirmado por el cliente.</summary>
/// <param name="OrderId">Pedido confirmado.</param>
/// <param name="Total">Importe total del pedido.</param>
/// <param name="LineCount">Cantidad de líneas.</param>
/// <param name="OccurredAt">Momento en que ocurrió.</param>
public sealed record OrderPlacedDomainEvent(
    OrderId OrderId,
    Money Total,
    int LineCount,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Un pedido fue cancelado.</summary>
/// <param name="OrderId">Pedido cancelado.</param>
/// <param name="OccurredAt">Momento en que ocurrió.</param>
public sealed record OrderCancelledDomainEvent(
    OrderId OrderId,
    DateTimeOffset OccurredAt) : IDomainEvent;
