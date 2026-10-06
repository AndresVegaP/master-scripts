using CleanArchitecture.Domain.Orders;
using CleanArchitecture.Domain.Products;

namespace CleanArchitecture.UnitTests.Fakes;

/// <summary>Repositorio de pedidos en memoria, también basado en snapshots.</summary>
internal sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly Dictionary<OrderId, OrderSnapshot> _rows = [];

    /// <summary>Cantidad de pedidos guardados.</summary>
    public int Count => _rows.Count;

    /// <summary>Guarda un pedido directamente, sin pasar por un caso de uso.</summary>
    public void Seed(Order order) => _rows[order.Id] = order.ToSnapshot();

    public Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_rows.TryGetValue(id, out var snapshot) ? Order.FromSnapshot(snapshot) : null);

    public Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        _rows[order.Id] = order.ToSnapshot();

        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        var snapshot = order.ToSnapshot();

        if (!_rows.TryGetValue(order.Id, out var current) || current.Version != snapshot.Version)
        {
            return Task.FromResult(false);
        }

        _rows[order.Id] = snapshot with { Version = snapshot.Version + 1 };

        return Task.FromResult(true);
    }

    public Task<bool> ExistsWithProductAsync(ProductId productId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_rows.Values.Any(order => order.Lines.Any(line => line.ProductId == productId.Value)));
}
