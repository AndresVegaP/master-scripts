namespace CleanArchitecture.Domain.Common;

/// <summary>
/// Un hecho que ocurrió en el dominio y que a otras partes del sistema les
/// interesa conocer (por ejemplo, "se confirmó un pedido").
/// </summary>
public interface IDomainEvent
{
    // 📘 docs/11-eventos-de-dominio.md
    //
    // Los eventos se nombran en PASADO (OrderPlaced, no PlaceOrder): describen
    // algo que YA sucedió. Quien los escucha no puede impedirlo, solo reaccionar.

    /// <summary>Momento en que ocurrió el hecho.</summary>
    DateTimeOffset OccurredAt { get; }
}
