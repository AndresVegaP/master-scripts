using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Application.Orders.PlaceOrder;
using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Orders;
using CleanArchitecture.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace CleanArchitecture.UnitTests.Application.Orders;

/// <summary>Tests del caso de uso "confirmar pedido".</summary>
public class PlaceOrderHandlerTests
{
    // Este es el caso de uso con más responsabilidades del repo: toca dos
    // agregados, una transacción y eventos de dominio. Por eso cada test
    // comprueba también los EFECTOS: qué se guardó, si se confirmó la
    // transacción y qué eventos se publicaron.

    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryOrderRepository _orders = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly RecordingDomainEventDispatcher _events = new();
    private readonly PlaceOrderHandler _sut;

    public PlaceOrderHandlerTests() =>
        _sut = new PlaceOrderHandler(_orders, _products, _unitOfWork, _events, new FakeTimeProvider(Some.Now));

    [Fact]
    public async Task ExecuteAsync_ConStockSuficiente_ConfirmaElPedido()
    {
        var keyboard = Some.Product(sku: "TEC-0001", price: 89990m, stock: 10);
        var monitor = Some.Product(sku: "MON-0001", price: 249990m, stock: 5);
        _products.Seed(keyboard);
        _products.Seed(monitor);

        var result = await _sut.ExecuteAsync(new PlaceOrderCommand("CLP",
        [
            new PlaceOrderLine(keyboard.Id.Value, 2),
            new PlaceOrderLine(monitor.Id.Value, 1),
        ]));

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(OrderStatus.Placed), result.Value.Status);
        Assert.Equal(2, result.Value.Lines.Count);
        Assert.Equal(429970m, result.Value.Total); // 89990 * 2 + 249990
    }

    [Fact]
    public async Task ExecuteAsync_ConStockSuficiente_DescuentaElStockYGuardaTodo()
    {
        var keyboard = Some.Product(stock: 10);
        _products.Seed(keyboard);

        await _sut.ExecuteAsync(new PlaceOrderCommand("CLP", [new PlaceOrderLine(keyboard.Id.Value, 3)]));

        var saved = await _products.GetByIdAsync(keyboard.Id);
        Assert.Equal(7, saved!.Stock);
        Assert.Equal(1, _orders.Count);
        Assert.Equal(1, _unitOfWork.Commits); // todo dentro de UNA transacción confirmada
    }

    [Fact]
    public async Task ExecuteAsync_ConStockSuficiente_PublicaElEventoDespuesDeConfirmar()
    {
        var product = Some.Product(stock: 10);
        _products.Seed(product);

        await _sut.ExecuteAsync(new PlaceOrderCommand("CLP", [new PlaceOrderLine(product.Id.Value, 1)]));

        var published = Assert.Single(_events.Published);
        Assert.IsType<OrderPlacedDomainEvent>(published);
    }

    [Fact]
    public async Task ExecuteAsync_ConElMismoProductoEnVariasLineas_LoFusiona()
    {
        var product = Some.Product(stock: 10);
        _products.Seed(product);

        var result = await _sut.ExecuteAsync(new PlaceOrderCommand("CLP",
        [
            new PlaceOrderLine(product.Id.Value, 2),
            new PlaceOrderLine(product.Id.Value, 3),
        ]));

        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(5, line.Quantity);

        var saved = await _products.GetByIdAsync(product.Id);
        Assert.Equal(5, saved!.Stock); // se descontó una sola vez, 5 unidades
    }

    [Fact]
    public async Task ExecuteAsync_SinStockSuficiente_DevuelveConflictoConTodosLosFaltantes()
    {
        var keyboard = Some.Product(sku: "TEC-0001", stock: 1);
        var monitor = Some.Product(sku: "MON-0001", stock: 0);
        _products.Seed(keyboard);
        _products.Seed(monitor);

        var result = await _sut.ExecuteAsync(new PlaceOrderCommand("CLP",
        [
            new PlaceOrderLine(keyboard.Id.Value, 5),
            new PlaceOrderLine(monitor.Id.Value, 1),
        ]));

        Assert.True(result.IsFailure);
        Assert.Equal("Order.InsufficientStock", result.Error.Code);

        // El servicio de dominio revisa TODAS las líneas, así que el mensaje
        // menciona los dos productos y no solo el primero.
        Assert.Contains("TEC-0001", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("MON-0001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_SinStockSuficiente_NoGuardaNada()
    {
        var product = Some.Product(stock: 1);
        _products.Seed(product);

        await _sut.ExecuteAsync(new PlaceOrderCommand("CLP", [new PlaceOrderLine(product.Id.Value, 9)]));

        Assert.Equal(0, _orders.Count);
        Assert.Equal(1, (await _products.GetByIdAsync(product.Id))!.Stock);
        Assert.True(_unitOfWork.RolledBack); // se abrió la transacción y NO se confirmó
        Assert.Empty(_events.Published);     // tampoco se avisó a nadie
    }

    [Fact]
    public async Task ExecuteAsync_ConProductoInexistente_DevuelveNotFound()
    {
        var result = await _sut.ExecuteAsync(new PlaceOrderCommand("CLP", [new PlaceOrderLine(Guid.NewGuid(), 1)]));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Order.ProductNotFound", result.Error.Code);
        Assert.True(_unitOfWork.RolledBack);
    }

    [Fact]
    public async Task ExecuteAsync_SiOtraPeticionCambioElProducto_DevuelveConflictoYNoConfirma()
    {
        var product = Some.Product(stock: 10);
        _products.Seed(product);
        _products.SimulateConcurrentChange = true;

        var result = await _sut.ExecuteAsync(new PlaceOrderCommand("CLP", [new PlaceOrderLine(product.Id.Value, 1)]));

        Assert.True(result.IsFailure);
        Assert.Equal("Order.ConcurrencyConflict", result.Error.Code);
        Assert.True(_unitOfWork.RolledBack);
    }

    [Fact]
    public async Task ExecuteAsync_ConMonedaNoAdmitida_DejaSalirLaDomainException()
    {
        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _sut.ExecuteAsync(new PlaceOrderCommand("XYZ", [new PlaceOrderLine(Guid.NewGuid(), 1)])));

        Assert.Equal("Currency.NotSupported", exception.Code);
    }
}
