using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Application.Abstractions;

/// <summary>Publica los eventos de dominio que registró un agregado.</summary>
public interface IDomainEventDispatcher
{
    /// <summary>Entrega cada evento a todos los manejadores registrados.</summary>
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

/// <summary>Reacciona a un evento de dominio.</summary>
public interface IDomainEventHandler
{
    /// <summary>Procesa el evento (o lo ignora si no es de su tipo).</summary>
    Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

/// <summary>Base para escribir manejadores con seguridad de tipos.</summary>
/// <typeparam name="TDomainEvent">Evento concreto al que reacciona.</typeparam>
public abstract class DomainEventHandler<TDomainEvent> : IDomainEventHandler
    where TDomainEvent : IDomainEvent
{
    // 📘 docs/11-eventos-de-dominio.md
    //
    // Truco simple para no usar reflexión ni librerías: el despachador entrega
    // TODOS los eventos a TODOS los manejadores, y esta clase base descarta los
    // que no le corresponden. Quien escribe un manejador solo ve su evento, ya
    // con el tipo correcto:
    //
    //     internal sealed class NotifyOnOrderPlaced : DomainEventHandler<OrderPlacedDomainEvent>
    //     {
    //         protected override Task HandleAsync(OrderPlacedDomainEvent e, CancellationToken ct) => ...;
    //     }
    //
    // Con muchos eventos y manejadores se pasa a un despachador que resuelve
    // por tipo (o a una librería de mensajería), pero el concepto es este.

    /// <inheritdoc />
    public Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default) =>
        domainEvent is TDomainEvent typedEvent
            ? HandleAsync(typedEvent, cancellationToken)
            : Task.CompletedTask;

    /// <summary>Procesa el evento que interesa a este manejador.</summary>
    protected abstract Task HandleAsync(TDomainEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>Despachador en proceso: entrega cada evento a cada manejador registrado.</summary>
internal sealed class DomainEventDispatcher(IEnumerable<IDomainEventHandler> handlers) : IDomainEventDispatcher
{
    public async Task DispatchAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (var domainEvent in domainEvents)
        {
            foreach (var handler in handlers)
            {
                await handler.HandleAsync(domainEvent, cancellationToken);
            }
        }
    }
}
