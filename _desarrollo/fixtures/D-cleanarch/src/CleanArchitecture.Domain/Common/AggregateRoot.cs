namespace CleanArchitecture.Domain.Common;

/// <summary>
/// Clase base de las raíces de agregado: la única puerta de entrada para
/// modificar un grupo de objetos que debe mantenerse consistente.
/// </summary>
/// <typeparam name="TId">Tipo del identificador fuertemente tipado.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : struct, IEquatable<TId>
{
    // 📘 docs/05-agregados.md
    //
    // Un AGREGADO es un grupo de objetos que cambian juntos y cuyas reglas
    // deben cumplirse SIEMPRE a la vez (por ejemplo, un pedido y sus líneas).
    // La RAÍZ es la entidad por la que se entra: nadie modifica una línea
    // directamente, se le pide al pedido. De ahí salen tres reglas prácticas:
    //   1. Los repositorios guardan y cargan agregados COMPLETOS.
    //   2. Una transacción de base de datos deja UN agregado consistente.
    //   3. La raíz registra los EVENTOS DE DOMINIO y lleva la VERSIÓN.

    /// <summary>Versión con la que nace un agregado que todavía no se guardó.</summary>
    protected const int InitialVersion = 1;

    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id, int version)
        : base(id)
    {
        Version = version;
    }

    /// <summary>
    /// Versión para la concurrencia optimista: el repositorio solo guarda los
    /// cambios si en la base de datos sigue estando esta misma versión.
    /// </summary>
    public int Version { get; }

    /// <summary>Eventos de dominio ocurridos y todavía no publicados.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Entrega los eventos pendientes y vacía la lista, para que cada evento
    /// se publique una sola vez.
    /// </summary>
    public IReadOnlyList<IDomainEvent> PullDomainEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }

    /// <summary>Registra que ocurrió algo relevante para el negocio.</summary>
    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
}
