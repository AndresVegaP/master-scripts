using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Products;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.Domain.Orders;

/// <summary>Una línea de pedido: qué producto, a qué precio y cuántas unidades.</summary>
public sealed class OrderLine : Entity<OrderLineId>
{
    // 📘 docs/05-agregados.md
    //
    // DOS IDEAS CLAVE DE DDD EN ESTA CLASE:
    //
    // 1. REFERENCIA A OTRO AGREGADO POR IDENTIDAD. La línea guarda un
    //    ProductId, NO un Product. Si guardara el objeto entero, cargar un
    //    pedido arrastraría productos, y modificar un producto desde un pedido
    //    rompería la frontera del agregado (y la transacción). Entre agregados
    //    se viaja por Id.
    //
    // 2. PRECIO CONGELADO. UnitPrice es una COPIA del precio que tenía el
    //    producto cuando se armó el pedido. Si mañana sube el precio del
    //    producto, este pedido no cambia: lo que se acordó, se acordó. Es un
    //    ejemplo de por qué copiar datos entre agregados a veces es lo correcto.
    //
    // Los métodos que MODIFICAN son "internal": solo el propio dominio (en la
    // práctica, la raíz Order) puede llamarlos. Ni la Api ni Infrastructure
    // pueden tocar una línea por su cuenta.

    /// <summary>Máximo de unidades por línea.</summary>
    public const int MaxQuantity = 1000;

    private OrderLine(OrderLineId id, ProductId productId, Money unitPrice, int quantity)
        : base(id)
    {
        ProductId = productId;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    /// <summary>Producto pedido, referenciado por su identidad.</summary>
    public ProductId ProductId { get; }

    /// <summary>Precio unitario acordado al armar el pedido.</summary>
    public Money UnitPrice { get; }

    /// <summary>Unidades pedidas.</summary>
    public int Quantity { get; private set; }

    /// <summary>Importe de la línea: precio unitario por cantidad.</summary>
    public Money Subtotal => UnitPrice.Multiply(Quantity);

    internal static OrderLine Create(ProductId productId, Money unitPrice, int quantity)
    {
        EnsureValidQuantity(quantity);

        return new OrderLine(OrderLineId.New(), productId, unitPrice, quantity);
    }

    internal void IncreaseQuantity(int quantity)
    {
        EnsureValidQuantity(quantity);
        EnsureValidQuantity(Quantity + quantity);

        Quantity += quantity;
    }

    internal OrderLineSnapshot ToSnapshot() =>
        new(Id.Value, ProductId.Value, UnitPrice.Amount, Quantity);

    internal static OrderLine FromSnapshot(OrderLineSnapshot snapshot, Currency currency) =>
        new(
            new OrderLineId(snapshot.Id),
            new ProductId(snapshot.ProductId),
            Money.Create(snapshot.UnitPrice, currency),
            snapshot.Quantity);

    private static void EnsureValidQuantity(int quantity)
    {
        if (quantity is < 1 or > MaxQuantity)
        {
            throw new DomainException(OrderErrors.QuantityOutOfRange(quantity, MaxQuantity));
        }
    }
}
