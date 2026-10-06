using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Application.Orders.CancelOrder;
using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Orders;
using CleanArchitecture.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace CleanArchitecture.UnitTests.Application.Orders;

/// <summary>Tests del caso de uso "cancelar pedido".</summary>
public class CancelOrderHandlerTests
{
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryOrderRepository _orders = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly RecordingDomainEventDispatcher _events = new();
    private readonly CancelOrderHandler _sut;

    public CancelOrderHandlerTests() =>
        _sut = new CancelOrderHandler(_orders, _products, _unitOfWork, _events, new FakeTimeProvider(Some.Now));

    [Fact]
    public async Task ExecuteAsync_ConPedidoConfirmado_LoCancelaYDevuelveElStock()
    {
        var product = Some.Product(stock: 8);
        _products.Seed(product);

        var order = Some.PlacedOrder(product.Id, quantity: 3);
        _orders.Seed(order);

        var result = await _sut.ExecuteAsync(new CancelOrderCommand(order.Id.Value));

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(OrderStatus.Cancelled), result.Value.Status);

        // El stock vuelve: 8 + 3. (Al sembrar el pedido no se descontó, así que
        // aquí lo que se comprueba es que la cancelación SUMA las unidades.)
        Assert.Equal(11, (await _products.GetByIdAsync(product.Id))!.Stock);

        var saved = await _orders.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, saved!.Status);
        Assert.Equal(1, _unitOfWork.Commits);
        Assert.IsType<OrderCancelledDomainEvent>(Assert.Single(_events.Published));
    }

    [Fact]
    public async Task ExecuteAsync_ConPedidoInexistente_DevuelveNotFound()
    {
        var result = await _sut.ExecuteAsync(new CancelOrderCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Order.NotFound", result.Error.Code);
        Assert.True(_unitOfWork.RolledBack);
    }

    [Fact]
    public async Task ExecuteAsync_ConPedidoYaCancelado_DejaSalirLaDomainException()
    {
        var product = Some.Product();
        _products.Seed(product);

        var order = Some.PlacedOrder(product.Id);
        order.Cancel(Some.Now);
        _orders.Seed(order);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _sut.ExecuteAsync(new CancelOrderCommand(order.Id.Value)));

        Assert.Equal("Order.NotPlaced", exception.Code);
        Assert.Equal(0, _unitOfWork.Commits);
    }
}
