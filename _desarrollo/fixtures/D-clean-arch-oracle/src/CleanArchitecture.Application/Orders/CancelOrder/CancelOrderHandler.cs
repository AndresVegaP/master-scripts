using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Domain.Orders;
using CleanArchitecture.Domain.Products;

namespace CleanArchitecture.Application.Orders.CancelOrder;

/// <summary>Caso de uso: cancelar un pedido y devolver las unidades al stock.</summary>
public sealed class CancelOrderHandler(
    IOrderRepository orderRepository,
    IProductRepository productRepository,
    IUnitOfWork unitOfWork,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider)
{
    /// <summary>Ejecuta el caso de uso.</summary>
    public async Task<Result<OrderDto>> ExecuteAsync(
        CancelOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();

        await unitOfWork.BeginTransactionAsync(cancellationToken);

        var order = await orderRepository.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);

        if (order is null)
        {
            return Result.Failure<OrderDto>(OrderUseCaseErrors.NotFound(command.OrderId));
        }

        // El AGREGADO decide si se puede cancelar (solo si está confirmado) y
        // registra el evento. Si no se puede, lanza DomainException → 409.
        order.Cancel(now);

        // Devolver las unidades al stock: la otra mitad de lo que hizo
        // PlaceOrderHandler, dentro de la misma transacción.
        foreach (var line in order.Lines)
        {
            var product = await productRepository.GetByIdAsync(line.ProductId, cancellationToken);

            if (product is null)
            {
                // No debería ocurrir: DeleteProduct impide borrar productos que
                // aparecen en pedidos. Si pasa, es mejor enterarse.
                return Result.Failure<OrderDto>(OrderUseCaseErrors.ProductNotFound(line.ProductId.Value));
            }

            product.AddStock(line.Quantity, now);

            if (!await productRepository.UpdateAsync(product, cancellationToken))
            {
                return Result.Failure<OrderDto>(OrderUseCaseErrors.ConcurrencyConflict(command.OrderId));
            }
        }

        if (!await orderRepository.UpdateAsync(order, cancellationToken))
        {
            return Result.Failure<OrderDto>(OrderUseCaseErrors.ConcurrencyConflict(command.OrderId));
        }

        await unitOfWork.CommitAsync(cancellationToken);

        await domainEventDispatcher.DispatchAsync(order.PullDomainEvents(), cancellationToken);

        return OrderDto.FromOrder(order);
    }
}
