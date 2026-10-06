using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Products;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.Domain.Orders;

/// <summary>Pedido de compra. Es la raíz del agregado Order (pedido + sus líneas).</summary>
public sealed class Order : AggregateRoot<OrderId>
{
    // 📘 docs/05-agregados.md y docs/10-transacciones-y-concurrencia.md
    //
    // Este es el agregado que Product no puede enseñar, porque aquí SÍ hay
    // varios objetos que deben mantenerse consistentes entre sí:
    //
    //   Order (raíz)
    //     └── OrderLine, OrderLine, ...   (entidades hijas)
    //
    // REGLAS DEL AGREGADO (invariantes que valen SIEMPRE, no "casi siempre"):
    //   - Todas las líneas están en la moneda del pedido.
    //   - Un producto aparece en una sola línea (si se repite, se suman unidades).
    //   - Como máximo MaxLines productos distintos.
    //   - Un pedido confirmado o cancelado ya no admite cambios en sus líneas.
    //   - No se confirma un pedido sin líneas.
    //
    // Fíjate en lo que NO hay: ningún método público devuelve la lista interna
    // para que alguien la modifique, y OrderLine solo se puede tocar desde
    // aquí. Esa es la diferencia entre "tener una lista" y "ser un agregado".

    /// <summary>Máximo de productos distintos en un pedido.</summary>
    public const int MaxLines = 20;

    private readonly List<OrderLine> _lines;

    private Order(
        OrderId id,
        Currency currency,
        OrderStatus status,
        List<OrderLine> lines,
        DateTimeOffset createdAt,
        DateTimeOffset? placedAt,
        DateTimeOffset? cancelledAt,
        int version)
        : base(id, version)
    {
        Currency = currency;
        Status = status;
        _lines = lines;
        CreatedAt = createdAt;
        PlacedAt = placedAt;
        CancelledAt = cancelledAt;
    }

    /// <summary>Moneda del pedido. Todas sus líneas usan esta moneda.</summary>
    public Currency Currency { get; }

    /// <summary>Estado actual del pedido.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>
    /// Líneas del pedido, de solo lectura. Devolvemos una vista de solo lectura
    /// y no la lista interna: si devolviéramos List, cualquiera podría agregar
    /// líneas saltándose las reglas de AddLine.
    /// </summary>
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    /// <summary>Importe total del pedido, calculado a partir de sus líneas.</summary>
    public Money Total => _lines.Aggregate(Money.Zero(Currency), (total, line) => total.Add(line.Subtotal));

    /// <summary>Momento de creación.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Momento de confirmación, o null si todavía es un borrador.</summary>
    public DateTimeOffset? PlacedAt { get; private set; }

    /// <summary>Momento de cancelación, o null si no se canceló.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Abre un pedido nuevo en estado borrador.</summary>
    public static Order Create(Currency currency, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(currency);

        return new Order(
            OrderId.New(),
            currency,
            OrderStatus.Draft,
            lines: [],
            createdAt: now,
            placedAt: null,
            cancelledAt: null,
            version: InitialVersion);
    }

    /// <summary>Agrega un producto al pedido, o suma unidades si ya estaba.</summary>
    /// <param name="productId">Producto que se agrega.</param>
    /// <param name="unitPrice">Precio unitario acordado (una copia del precio del producto).</param>
    /// <param name="quantity">Unidades a agregar.</param>
    /// <exception cref="DomainException">Si el pedido no es un borrador, la moneda no coincide, la cantidad está fuera de rango, el precio no coincide con el de la línea existente o se supera el máximo de líneas.</exception>
    public void AddLine(ProductId productId, Money unitPrice, int quantity)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);
        EnsureStatus(OrderStatus.Draft, OrderErrors.NotDraft(Status));

        if (unitPrice.Currency != Currency)
        {
            throw new DomainException(OrderErrors.LineCurrencyMismatch(Currency, unitPrice.Currency));
        }

        var existingLine = _lines.Find(line => line.ProductId == productId);

        if (existingLine is not null)
        {
            // Un producto, una línea: si vuelve a llegar, se suman unidades.
            if (existingLine.UnitPrice != unitPrice)
            {
                throw new DomainException(OrderErrors.LinePriceMismatch(existingLine.UnitPrice, unitPrice));
            }

            existingLine.IncreaseQuantity(quantity);
            return;
        }

        if (_lines.Count >= MaxLines)
        {
            throw new DomainException(OrderErrors.TooManyLines(MaxLines));
        }

        _lines.Add(OrderLine.Create(productId, unitPrice, quantity));
    }

    /// <summary>Confirma el pedido: a partir de aquí ya no admite cambios.</summary>
    /// <exception cref="DomainException">Si no es un borrador o no tiene líneas.</exception>
    public void Place(DateTimeOffset now)
    {
        EnsureStatus(OrderStatus.Draft, OrderErrors.NotDraft(Status));

        if (_lines.Count == 0)
        {
            throw new DomainException(OrderErrors.NoLines);
        }

        Status = OrderStatus.Placed;
        PlacedAt = now;

        // El agregado ANUNCIA lo que pasó; no sabe quién escucha ni qué hará.
        Raise(new OrderPlacedDomainEvent(Id, Total, _lines.Count, now));
    }

    /// <summary>Cancela un pedido confirmado.</summary>
    /// <exception cref="DomainException">Si el pedido no está confirmado.</exception>
    public void Cancel(DateTimeOffset now)
    {
        EnsureStatus(OrderStatus.Placed, OrderErrors.NotPlaced(Status));

        Status = OrderStatus.Cancelled;
        CancelledAt = now;

        Raise(new OrderCancelledDomainEvent(Id, now));
    }

    /// <summary>Entrega una copia plana del estado actual, líneas incluidas.</summary>
    public OrderSnapshot ToSnapshot() =>
        new(
            Id.Value,
            Currency.Code,
            Status,
            CreatedAt,
            PlacedAt,
            CancelledAt,
            Version,
            [.. _lines.Select(line => line.ToSnapshot())]);

    /// <summary>Reconstruye un pedido que YA EXISTE a partir de su estado guardado.</summary>
    /// <exception cref="CorruptedSnapshotException">Si el estado guardado no cumple las reglas actuales.</exception>
    public static Order FromSnapshot(OrderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            var currency = Currency.Create(snapshot.Currency);
            var lines = snapshot.Lines
                .Select(line => OrderLine.FromSnapshot(line, currency))
                .ToList();

            EnsureConsistentSnapshot(snapshot, lines);

            return new Order(
                new OrderId(snapshot.Id),
                currency,
                snapshot.Status,
                lines,
                snapshot.CreatedAt,
                snapshot.PlacedAt,
                snapshot.CancelledAt,
                snapshot.Version);
        }
        catch (DomainException exception)
        {
            throw new CorruptedSnapshotException(nameof(Order), snapshot.Id, exception);
        }
    }

    private static void EnsureConsistentSnapshot(OrderSnapshot snapshot, List<OrderLine> lines)
    {
        // Comprobaciones mínimas de integridad: no revalidamos las reglas de
        // creación (rehidratar no es crear), pero sí que el estado guardado sea
        // coherente consigo mismo.
        var inconsistent = snapshot.Status switch
        {
            OrderStatus.Placed => snapshot.PlacedAt is null || lines.Count == 0,
            OrderStatus.Cancelled => snapshot.CancelledAt is null || lines.Count == 0,
            _ => false,
        };

        if (inconsistent)
        {
            throw new DomainException(OrderErrors.NoLines);
        }
    }

    private void EnsureStatus(OrderStatus expected, DomainError error)
    {
        if (Status != expected)
        {
            throw new DomainException(error);
        }
    }
}
