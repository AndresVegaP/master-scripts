namespace CleanArchitecture.Domain.Orders;

/// <summary>Foto inmutable de una línea de pedido.</summary>
/// <param name="Id">Identidad de la línea.</param>
/// <param name="ProductId">Producto referenciado.</param>
/// <param name="UnitPrice">Importe del precio unitario acordado.</param>
/// <param name="Quantity">Unidades pedidas.</param>
public sealed record OrderLineSnapshot(Guid Id, Guid ProductId, decimal UnitPrice, int Quantity);

/// <summary>Foto inmutable del estado de un <see cref="Order"/>, incluidas sus líneas.</summary>
/// <param name="Id">Identidad del pedido.</param>
/// <param name="Currency">Código ISO de la moneda del pedido.</param>
/// <param name="Status">Estado actual.</param>
/// <param name="CreatedAt">Momento de creación.</param>
/// <param name="PlacedAt">Momento de confirmación, o null.</param>
/// <param name="CancelledAt">Momento de cancelación, o null.</param>
/// <param name="Version">Versión para la concurrencia optimista.</param>
/// <param name="Lines">Líneas del pedido.</param>
public sealed record OrderSnapshot(
    Guid Id,
    string Currency,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PlacedAt,
    DateTimeOffset? CancelledAt,
    int Version,
    IReadOnlyList<OrderLineSnapshot> Lines)
{
    // 📘 docs/06-repositorios-dapper-y-snapshots.md
    //
    // AQUÍ SE VE POR QUÉ EL PATRÓN SNAPSHOT VALE LA PENA. Un agregado con
    // hijos no se puede rehidratar con una lista de parámetros: harían falta
    // firmas enormes y el repositorio tendría que saber construir las líneas.
    // Con el snapshot, el agregado entrega (y recibe) UN objeto con todo su
    // estado, incluidas las líneas, y el repositorio solo traduce eso a dos
    // tablas: Orders y OrderLines.
    //
    // La moneda viaja SOLO en el pedido, no en cada línea: dentro del agregado
    // todas las líneas comparten la moneda del pedido. El snapshot refleja las
    // reglas del agregado, no la comodidad de la base de datos.
    //
    // TRAMPA CONOCIDA DE LOS RECORDS: dos records se comparan por valor, pero
    // una propiedad de tipo lista se compara POR REFERENCIA. Es decir:
    //
    //     snapshotA == snapshotB   // false aunque las líneas sean idénticas
    //
    // En los tests compara las líneas aparte (por ejemplo, con
    // Assert.Equal(esperadas, actuales), que compara secuencias elemento a
    // elemento). Lo explica docs/09-tests.md.
}
