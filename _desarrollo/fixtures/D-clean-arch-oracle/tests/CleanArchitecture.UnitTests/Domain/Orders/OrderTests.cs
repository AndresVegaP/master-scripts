using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Orders;
using CleanArchitecture.Domain.Products;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.UnitTests.Domain.Orders;

/// <summary>Tests del agregado Order: la raíz, sus líneas y su ciclo de vida.</summary>
public class OrderTests
{
    private static readonly ProductId Keyboard = ProductId.New();
    private static readonly ProductId Monitor = ProductId.New();

    [Fact]
    public void Create_EmpiezaEnBorradorSinLineasYConTotalCero()
    {
        var order = Some.DraftOrder();

        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Empty(order.Lines);
        Assert.Equal(0m, order.Total.Amount);
        Assert.Null(order.PlacedAt);
    }

    [Fact]
    public void AddLine_AgregaLaLineaYCalculaElSubtotal()
    {
        var order = Some.DraftOrder();

        order.AddLine(Keyboard, Some.Money(89990m), 2);

        var line = Assert.Single(order.Lines);
        Assert.Equal(Keyboard, line.ProductId);
        Assert.Equal(179980m, line.Subtotal.Amount);
        Assert.Equal(179980m, order.Total.Amount);
    }

    [Fact]
    public void AddLine_ConElMismoProductoDosVeces_SumaLasUnidadesEnUnaSolaLinea()
    {
        // Invariante del agregado: un producto, una línea.
        var order = Some.DraftOrder();

        order.AddLine(Keyboard, Some.Money(89990m), 1);
        order.AddLine(Keyboard, Some.Money(89990m), 2);

        var line = Assert.Single(order.Lines);
        Assert.Equal(3, line.Quantity);
    }

    [Fact]
    public void AddLine_ConOtroPrecioParaElMismoProducto_LanzaDomainException()
    {
        var order = Some.DraftOrder();
        order.AddLine(Keyboard, Some.Money(89990m), 1);

        var exception = Assert.Throws<DomainException>(() => order.AddLine(Keyboard, Some.Money(79990m), 1));

        Assert.Equal("Order.LinePriceMismatch", exception.Code);
    }

    [Fact]
    public void AddLine_ConOtraMoneda_LanzaDomainException()
    {
        var order = Some.DraftOrder("CLP");

        var exception = Assert.Throws<DomainException>(
            () => order.AddLine(Keyboard, Money.Create(10m, "USD"), 1));

        Assert.Equal("Order.LineCurrencyMismatch", exception.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(OrderLine.MaxQuantity + 1)]
    public void AddLine_ConCantidadFueraDeRango_LanzaDomainException(int quantity)
    {
        var exception = Assert.Throws<DomainException>(
            () => Some.DraftOrder().AddLine(Keyboard, Some.Money(), quantity));

        Assert.Equal("Order.QuantityOutOfRange", exception.Code);
    }

    [Fact]
    public void AddLine_SuperandoElMaximoDeLineas_LanzaDomainException()
    {
        var order = Some.DraftOrder();

        for (var i = 0; i < Order.MaxLines; i++)
        {
            order.AddLine(ProductId.New(), Some.Money(), 1);
        }

        var exception = Assert.Throws<DomainException>(() => order.AddLine(ProductId.New(), Some.Money(), 1));

        Assert.Equal("Order.TooManyLines", exception.Code);
    }

    [Fact]
    public void Lines_NoSePuedeModificarDesdeFuera()
    {
        // El agregado expone una vista de SOLO LECTURA. Si devolviera su List
        // interna, cualquiera podría agregar líneas saltándose AddLine y sus
        // reglas: dejaría de ser un agregado.
        var order = Some.DraftOrder();
        order.AddLine(Keyboard, Some.Money(), 1);

        Assert.IsNotType<List<OrderLine>>(order.Lines);
    }

    [Fact]
    public void Place_ConLineas_ConfirmaElPedidoYPublicaElEvento()
    {
        var order = Some.DraftOrder();
        order.AddLine(Keyboard, Some.Money(89990m), 2);

        order.Place(Some.Now);

        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(Some.Now, order.PlacedAt);

        var domainEvent = Assert.Single(order.DomainEvents);
        var placed = Assert.IsType<OrderPlacedDomainEvent>(domainEvent);
        Assert.Equal(order.Id, placed.OrderId);
        Assert.Equal(179980m, placed.Total.Amount);
        Assert.Equal(1, placed.LineCount);
    }

    [Fact]
    public void Place_SinLineas_LanzaDomainException()
    {
        var exception = Assert.Throws<DomainException>(() => Some.DraftOrder().Place(Some.Now));

        Assert.Equal("Order.NoLines", exception.Code);
    }

    [Fact]
    public void AddLine_DespuesDeConfirmar_LanzaDomainException()
    {
        var order = Some.PlacedOrder(Keyboard);

        var exception = Assert.Throws<DomainException>(() => order.AddLine(Monitor, Some.Money(), 1));

        Assert.Equal("Order.NotDraft", exception.Code);
    }

    [Fact]
    public void Cancel_ConPedidoConfirmado_LoCancelaYPublicaElEvento()
    {
        var order = Some.PlacedOrder(Keyboard);
        order.PullDomainEvents(); // descartamos el evento de confirmación

        order.Cancel(Some.Now.AddHours(1));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(Some.Now.AddHours(1), order.CancelledAt);
        Assert.IsType<OrderCancelledDomainEvent>(Assert.Single(order.DomainEvents));
    }

    [Fact]
    public void Cancel_ConPedidoEnBorrador_LanzaDomainException()
    {
        var exception = Assert.Throws<DomainException>(() => Some.DraftOrder().Cancel(Some.Now));

        Assert.Equal("Order.NotPlaced", exception.Code);
    }

    [Fact]
    public void Cancel_DosVeces_LanzaDomainException()
    {
        var order = Some.PlacedOrder(Keyboard);
        order.Cancel(Some.Now);

        Assert.Equal("Order.NotPlaced", Assert.Throws<DomainException>(() => order.Cancel(Some.Now)).Code);
    }

    [Fact]
    public void PullDomainEvents_DevuelveLosEventosYVaciaLaLista()
    {
        // Cada evento se publica UNA sola vez: quien los saca se los lleva.
        var order = Some.PlacedOrder(Keyboard);

        Assert.Single(order.PullDomainEvents());
        Assert.Empty(order.DomainEvents);
        Assert.Empty(order.PullDomainEvents());
    }

    [Fact]
    public void Total_SumaLosSubtotalesDeTodasLasLineas()
    {
        var order = Some.DraftOrder();
        order.AddLine(Keyboard, Some.Money(89990m), 2);  // 179980
        order.AddLine(Monitor, Some.Money(249990m), 1);  // 249990

        Assert.Equal(429970m, order.Total.Amount);
    }

    [Fact]
    public void ToSnapshot_YFromSnapshot_ConservanElPedidoConSusLineas()
    {
        var original = Some.DraftOrder();
        original.AddLine(Keyboard, Some.Money(89990m), 2);
        original.AddLine(Monitor, Some.Money(249990m), 1);
        original.Place(Some.Now);

        var snapshot = original.ToSnapshot();
        var rehydrated = Order.FromSnapshot(snapshot);

        Assert.Equal(original.Id, rehydrated.Id);
        Assert.Equal(original.Status, rehydrated.Status);
        Assert.Equal(original.Total, rehydrated.Total);
        Assert.Equal(original.PlacedAt, rehydrated.PlacedAt);

        // TRAMPA DE LOS RECORDS: snapshot == rehydrated.ToSnapshot() sería FALSE,
        // porque la propiedad Lines es una lista y los records comparan las
        // listas por REFERENCIA. Hay que comparar la secuencia aparte.
        Assert.Equal(snapshot.Lines, rehydrated.ToSnapshot().Lines);
        Assert.Equal(snapshot with { Lines = [] }, rehydrated.ToSnapshot() with { Lines = [] });
    }

    [Fact]
    public void FromSnapshot_ConEstadoIncoherente_LanzaCorruptedSnapshotException()
    {
        // Un pedido "confirmado" sin líneas no puede existir: si aparece en la
        // base de datos, los datos están corruptos.
        var corrupted = Some.PlacedOrder(Keyboard).ToSnapshot() with { Lines = [] };

        var exception = Assert.Throws<CorruptedSnapshotException>(() => Order.FromSnapshot(corrupted));

        Assert.Equal(nameof(Order), exception.AggregateName);
    }
}
