using System.Globalization;
using CleanArchitecture.Domain.Orders;
using CleanArchitecture.Domain.Products;
using CleanArchitecture.Infrastructure.Persistence.Dal;
using Dapper;

namespace CleanArchitecture.Infrastructure.Persistence.Orders;

/// <summary>Adaptador con Dapper del puerto <see cref="IOrderRepository"/>.</summary>
internal sealed class OrderRepository(DbSession session) : IOrderRepository
{
    // 📘 docs/10-transacciones-y-concurrencia.md
    //
    // AQUÍ SE VE QUÉ SIGNIFICA "EL AGREGADO SE GUARDA COMPLETO": el pedido vive
    // en dos tablas (Orders y OrderLines), pero para el dominio es UNA sola
    // cosa. Por eso:
    //   - GetByIdAsync trae pedido y líneas (dos consultas: Oracle no acepta
    //     varias sentencias sueltas en un mismo comando).
    //   - AddAsync y UpdateAsync escriben en las dos tablas dentro de la MISMA
    //     transacción, que abre el caso de uso a través de IUnitOfWork.
    //   - Al actualizar, las líneas se borran y se vuelven a insertar: el
    //     agregado manda, y su estado actual es la verdad. Es la estrategia más
    //     simple y la correcta para agregados pequeños.

    // Migrado de PCK_PEDIDOS.SP_OBTENER_CABECERA_PEDIDO
    // La descripción del estado se sigue calculando con la función del paquete.
    private const string ObtenerCabeceraSql =
        """
        SELECT o.Id, o.Currency, o.Status, PCK_PEDIDOS.FN_ESTADO_DESC(o.Status) AS StatusDescription,
               o.CreatedAt, o.PlacedAt, o.CancelledAt, o.Version
        FROM Orders o
        WHERE o.Id = :Id
        """;

    public async Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);

        var orderRow = await connection.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition(
            ObtenerCabeceraSql,
            new { Id = id.Value.ToString() },
            session.Transaction,
            cancellationToken: cancellationToken));

        if (orderRow is null)
        {
            return null;
        }

        var lineRows = await connection.QueryAsync<OrderLineRow>(new CommandDefinition(
            """
            SELECT Id, ProductId, UnitPrice, Quantity
            FROM OrderLines
            WHERE OrderId = :Id
            ORDER BY Position
            """,
            new { Id = id.Value.ToString() },
            session.Transaction,
            cancellationToken: cancellationToken));

        return Order.FromSnapshot(orderRow.ToSnapshot([.. lineRows.Select(line => line.ToSnapshot())]));
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var connection = await session.GetConnectionAsync(cancellationToken);
        var snapshot = order.ToSnapshot();

        // Migrado de PCK_PEDIDOS.SP_INSERTAR_PEDIDO (alta de la cabecera).
        // Las líneas, que el procedimiento insertaba en un bucle, van en
        // InsertLinesAsync.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO Orders (Id, Currency, Status, CreatedAt, PlacedAt, CancelledAt, Version)
            VALUES (:Id, :Currency, :Status, :CreatedAt, :PlacedAt, :CancelledAt, :Version)
            """,
            OrderRow.ToParameters(snapshot),
            session.Transaction,
            cancellationToken: cancellationToken));

        await InsertLinesAsync(connection, snapshot, cancellationToken);

        // Auditoría, asiento contable y resumen de ventas: siguen en la base
        // (PCK_PEDIDOS.SP_REGISTRAR_AUDITORIA, el procedimiento de ventas del ERP
        // por db link y el refresco del resumen diario). Van en la MISMA
        // transacción: si el ERP rechaza el asiento, no queda pedido a medias.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            BEGIN
                PCK_PEDIDOS.SP_REGISTRAR_AUDITORIA(:Id, 'ALTA');
                PCK_CONTABILIDAD.SP_REGISTRAR_VENTA@ERP_LINK(:Id, :Total);
                PRC_ACTUALIZAR_RESUMEN_VENTAS;
            END;
            """,
            new
            {
                Id = snapshot.Id.ToString(),
                Total = snapshot.Lines.Sum(line => line.UnitPrice * line.Quantity),
            },
            session.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var connection = await session.GetConnectionAsync(cancellationToken);
        var snapshot = order.ToSnapshot();

        var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            """
            -- Migrado de PCK_PEDIDOS.SP_ACTUALIZAR_ESTADO_PEDIDO
            UPDATE Orders
            SET Status = :Status,
                PlacedAt = :PlacedAt,
                CancelledAt = :CancelledAt,
                Version = Version + 1
            WHERE Id = :Id AND Version = :Version
            """,
            OrderRow.ToParameters(snapshot),
            session.Transaction,
            cancellationToken: cancellationToken));

        if (affectedRows == 0)
        {
            // O el pedido ya no existe, o alguien lo modificó antes que nosotros.
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM OrderLines WHERE OrderId = :OrderId",
            new { OrderId = snapshot.Id.ToString() },
            session.Transaction,
            cancellationToken: cancellationToken));

        await InsertLinesAsync(connection, snapshot, cancellationToken);

        // Las unidades devueltas cambian el stock consolidado del almacén: el
        // recálculo sigue siendo un procedimiento de la base de datos.
        await InventarioDal.RecalcularStockAsync(connection, session.Transaction, snapshot.Id, cancellationToken);

        return true;
    }

    public async Task<bool> ExistsWithProductAsync(ProductId productId, CancellationToken cancellationToken = default)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);

        // Solo cuentan las líneas activas: las de pedidos anulados en el sistema
        // viejo no deberían impedir la baja del producto.
        var activeLines = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM OrderLines l
            WHERE l.ProductId = :ProductId
              AND VENTAS.FN_LINEA_ACTIVA(l.Id) = 'S'
            """,
            new { ProductId = productId.Value.ToString() },
            session.Transaction,
            cancellationToken: cancellationToken));

        return activeLines > 0;
    }

    private async Task InsertLinesAsync(
        System.Data.Common.DbConnection connection,
        OrderSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.Lines.Count == 0)
        {
            return;
        }

        // Dapper ejecuta la sentencia una vez por elemento cuando los
        // parámetros son una colección.
        //
        // Position guarda el ORDEN de las líneas dentro del agregado. Ordenar
        // por Id no serviría: los identificadores son UUID versión 7, que solo
        // quedan ordenados entre milisegundos distintos; dos líneas creadas en
        // el mismo instante podrían volver al revés. Y un agregado tiene que
        // reconstruirse EXACTAMENTE como se guardó.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO OrderLines (Id, OrderId, Position, ProductId, UnitPrice, Quantity)
            VALUES (:Id, :OrderId, :Position, :ProductId, :UnitPrice, :Quantity)
            """,
            snapshot.Lines.Select((line, position) => new
            {
                Id = line.Id.ToString(),
                OrderId = snapshot.Id.ToString(),
                Position = position,
                ProductId = line.ProductId.ToString(),
                line.UnitPrice,
                line.Quantity,
            }),
            session.Transaction,
            cancellationToken: cancellationToken));
    }

    private sealed class OrderRow
    {
        public string Id { get; init; } = string.Empty;

        public string Currency { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        // Texto legible del estado, calculado por la base. Hoy solo se registra
        // en el log de soporte; el agregado no lo necesita.
        public string? StatusDescription { get; init; }

        public string CreatedAt { get; init; } = string.Empty;

        public string? PlacedAt { get; init; }

        public string? CancelledAt { get; init; }

        public long Version { get; init; }

        public OrderSnapshot ToSnapshot(IReadOnlyList<OrderLineSnapshot> lines) =>
            new(
                Guid.Parse(Id),
                Currency,
                Enum.Parse<OrderStatus>(Status),
                DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture),
                PlacedAt is null ? null : DateTimeOffset.Parse(PlacedAt, CultureInfo.InvariantCulture),
                CancelledAt is null ? null : DateTimeOffset.Parse(CancelledAt, CultureInfo.InvariantCulture),
                (int)Version,
                lines);

        public static object ToParameters(OrderSnapshot snapshot) => new
        {
            Id = snapshot.Id.ToString(),
            snapshot.Currency,
            // El estado se guarda como texto ("Placed") y no como número: la base
            // de datos se puede leer sin tener que traducir códigos, y agregar un
            // estado nuevo no renumera los existentes.
            Status = snapshot.Status.ToString(),
            snapshot.CreatedAt,
            snapshot.PlacedAt,
            snapshot.CancelledAt,
            snapshot.Version,
        };
    }

    private sealed class OrderLineRow
    {
        public string Id { get; init; } = string.Empty;

        public string ProductId { get; init; } = string.Empty;

        public string UnitPrice { get; init; } = string.Empty;

        public long Quantity { get; init; }

        public OrderLineSnapshot ToSnapshot() =>
            new(
                Guid.Parse(Id),
                Guid.Parse(ProductId),
                decimal.Parse(UnitPrice, CultureInfo.InvariantCulture),
                (int)Quantity);
    }
}
