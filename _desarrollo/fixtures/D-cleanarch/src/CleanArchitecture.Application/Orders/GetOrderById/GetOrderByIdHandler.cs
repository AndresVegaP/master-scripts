using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Domain.Orders;

namespace CleanArchitecture.Application.Orders.GetOrderById;

/// <summary>Caso de uso: obtener un pedido con sus líneas.</summary>
public sealed class GetOrderByIdHandler(IOrderRepository orderRepository)
{
    /// <summary>Ejecuta el caso de uso.</summary>
    public async Task<Result<OrderDto>> ExecuteAsync(
        GetOrderByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // El repositorio devuelve el agregado COMPLETO: pedido y líneas. Para
        // el dominio son una sola cosa, aunque por debajo sean dos tablas.
        var order = await orderRepository.GetByIdAsync(new OrderId(query.OrderId), cancellationToken);

        return order is null
            ? Result.Failure<OrderDto>(OrderUseCaseErrors.NotFound(query.OrderId))
            : OrderDto.FromOrder(order);
    }
}
