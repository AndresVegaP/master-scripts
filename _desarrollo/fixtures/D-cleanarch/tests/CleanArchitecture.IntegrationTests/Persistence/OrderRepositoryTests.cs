using CleanArchitecture.Domain.Orders;
using CleanArchitecture.Domain.Products;
using CleanArchitecture.Domain.SharedKernel;
using CleanArchitecture.Infrastructure.Persistence.Orders;

namespace CleanArchitecture.IntegrationTests.Persistence;

/// <summary>Tests del repositorio de pedidos: un agregado repartido en dos tablas.</summary>
public class OrderRepositoryTests : SqliteTestDatabase
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProductId Keyboard = ProductId.New();
    private static readonly ProductId Monitor = ProductId.New();

    private static Order PlacedOrder()
    {
        var order = Order.Create(Currency.Create("CLP"), Now);
        order.AddLine(Keyboard, Money.Create(89990m, "CLP"), 2);
        order.AddLine(Monitor, Money.Create(249990m, "CLP"), 1);
        order.Place(Now);

        return order;
    }

    [Fact]
    public async Task AddAsync_YGetByIdAsync_ConservanElPedidoConSusLineas()
    {
        var repository = new OrderRepository(Session);
        var order = PlacedOrder();

        await repository.AddAsync(order, TestContext.Current.CancellationToken);
        var loaded = await repository.GetByIdAsync(order.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(OrderStatus.Placed, loaded.Status);
        Assert.Equal(order.Total, loaded.Total);
        Assert.Equal(2, loaded.Lines.Count);

        // Los snapshots de las líneas se comparan como secuencia (los records
        // comparan las listas por referencia, no por contenido).
        Assert.Equal(order.ToSnapshot().Lines, loaded.ToSnapshot().Lines);
    }

    [Fact]
    public async Task GetByIdAsync_ConIdInexistente_DevuelveNull()
    {
        var repository = new OrderRepository(Session);

        Assert.Null(await repository.GetByIdAsync(OrderId.New(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateAsync_GuardaElNuevoEstadoDelAgregado()
    {
        var repository = new OrderRepository(Session);
        var order = PlacedOrder();
        await repository.AddAsync(order, TestContext.Current.CancellationToken);

        order.Cancel(Now.AddHours(1));

        Assert.True(await repository.UpdateAsync(order, TestContext.Current.CancellationToken));

        var loaded = await repository.GetByIdAsync(order.Id, TestContext.Current.CancellationToken);
        Assert.Equal(OrderStatus.Cancelled, loaded!.Status);
        Assert.Equal(Now.AddHours(1), loaded.CancelledAt);
        // Las líneas se reescriben, pero siguen siendo las mismas: ni se pierden
        // ni se duplican.
        Assert.Equal(2, loaded.Lines.Count);
    }

    [Fact]
    public async Task UpdateAsync_ConUnaVersionYaSuperada_DevuelveFalse()
    {
        var repository = new OrderRepository(Session);
        var order = PlacedOrder();
        await repository.AddAsync(order, TestContext.Current.CancellationToken);

        var copiaA = await repository.GetByIdAsync(order.Id, TestContext.Current.CancellationToken);
        var copiaB = await repository.GetByIdAsync(order.Id, TestContext.Current.CancellationToken);

        copiaA!.Cancel(Now);
        Assert.True(await repository.UpdateAsync(copiaA, TestContext.Current.CancellationToken));

        copiaB!.Cancel(Now);
        Assert.False(await repository.UpdateAsync(copiaB, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsWithProductAsync_EncuentraLosPedidosQueUsanUnProducto()
    {
        var repository = new OrderRepository(Session);
        await repository.AddAsync(PlacedOrder(), TestContext.Current.CancellationToken);

        Assert.True(await repository.ExistsWithProductAsync(Keyboard, TestContext.Current.CancellationToken));
        Assert.False(await repository.ExistsWithProductAsync(ProductId.New(), TestContext.Current.CancellationToken));
    }
}
