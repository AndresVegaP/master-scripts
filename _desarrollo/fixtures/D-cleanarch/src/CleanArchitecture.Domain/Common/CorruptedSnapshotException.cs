namespace CleanArchitecture.Domain.Common;

/// <summary>
/// Se lanza cuando el estado guardado de un agregado no cumple las reglas
/// actuales del dominio y, por lo tanto, no se puede reconstruir.
/// </summary>
public sealed class CorruptedSnapshotException : Exception
{
    // 📘 docs/06-repositorios-dapper-y-snapshots.md
    //
    // ¿Por qué una excepción DISTINTA de DomainException? Porque la culpa es
    // de otro. Si alguien pide crear un producto con un SKU inválido, el error
    // es del cliente (4xx). Si un producto YA GUARDADO no se puede reconstruir
    // —se editó la base de datos a mano, o cambió una regla y falta una
    // migración— el error es NUESTRO: 500, alerta y revisión de los datos.
    // Nunca un 4xx: el cliente no hizo nada malo.

    public CorruptedSnapshotException(string aggregateName, Guid aggregateId, Exception innerException)
        : base(
            $"El estado guardado del agregado {aggregateName} {aggregateId} no cumple las reglas actuales del dominio.",
            innerException)
    {
        AggregateName = aggregateName;
        AggregateId = aggregateId;
    }

    /// <summary>Nombre del agregado que no se pudo reconstruir.</summary>
    public string AggregateName { get; }

    /// <summary>Identidad del agregado corrupto, para poder buscarlo en la base de datos.</summary>
    public Guid AggregateId { get; }
}
