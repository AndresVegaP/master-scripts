using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Domain.Orders;

namespace CleanArchitecture.Application.Orders.EventHandlers;

/// <summary>Avisa de que un pedido fue confirmado.</summary>
internal sealed class NotifyWhenOrderPlaced(INotificationSender notificationSender)
    : DomainEventHandler<OrderPlacedDomainEvent>
{
    // 📘 docs/11-eventos-de-dominio.md
    //
    // Aquí se ve para qué sirven los eventos de dominio: el pedido NO sabe que
    // existe este aviso. Solo anunció "me confirmaron"; quien quiera reaccionar
    // se suscribe. Mañana se agregan "descontar del presupuesto" o "avisar a
    // logística" sin tocar ni una línea de Order ni de PlaceOrderHandler.
    //
    // Eso es el principio abierto/cerrado (la O de SOLID) aplicado al dominio:
    // abierto a nuevas reacciones, cerrado a modificaciones.

    protected override Task HandleAsync(OrderPlacedDomainEvent domainEvent, CancellationToken cancellationToken) =>
        notificationSender.SendAsync(
            $"Pedido {domainEvent.OrderId} confirmado",
            $"Se confirmó el pedido {domainEvent.OrderId} con {domainEvent.LineCount} línea(s) por un total de {domainEvent.Total}.",
            cancellationToken);
}
