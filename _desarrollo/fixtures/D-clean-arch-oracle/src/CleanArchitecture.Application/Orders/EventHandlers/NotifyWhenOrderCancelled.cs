using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Domain.Orders;

namespace CleanArchitecture.Application.Orders.EventHandlers;

/// <summary>Avisa de que un pedido fue cancelado.</summary>
internal sealed class NotifyWhenOrderCancelled(INotificationSender notificationSender)
    : DomainEventHandler<OrderCancelledDomainEvent>
{
    protected override Task HandleAsync(OrderCancelledDomainEvent domainEvent, CancellationToken cancellationToken) =>
        notificationSender.SendAsync(
            $"Pedido {domainEvent.OrderId} cancelado",
            $"Se canceló el pedido {domainEvent.OrderId} y se devolvieron las unidades al stock.",
            cancellationToken);
}
