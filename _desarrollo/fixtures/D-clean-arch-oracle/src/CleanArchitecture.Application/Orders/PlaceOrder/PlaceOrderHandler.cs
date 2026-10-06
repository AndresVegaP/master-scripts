using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Domain.Orders;
using CleanArchitecture.Domain.Products;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.Application.Orders.PlaceOrder;

/// <summary>Caso de uso: confirmar un pedido y descontar el stock.</summary>
public sealed class PlaceOrderHandler(
    IOrderRepository orderRepository,
    IProductRepository productRepository,
    IUnitOfWork unitOfWork,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider)
{
    // 📘 docs/10-transacciones-y-concurrencia.md
    //
    // ESTE ES EL CASO DE USO MÁS INTERESANTE DEL REPO, porque toca DOS
    // agregados (el pedido y varios productos) y por lo tanto obliga a decidir
    // cosas que con un solo agregado no aparecen:
    //
    //  1. TRANSACCIÓN: o se guarda el pedido Y el stock descontado, o no se
    //     guarda nada. Si no, se vendería stock que no existe.
    //  2. REGLA DE DDD: la ortodoxia dice "modifica UN agregado por
    //     transacción" y deja los demás en consistencia eventual (por eventos).
    //     Aquí elegimos a conciencia la vía pragmática —una transacción para
    //     ambos— porque el dominio es pequeño y la consistencia inmediata del
    //     stock es lo que el negocio espera. El razonamiento completo, con sus
    //     contras, está en docs/adr/0005-un-agregado-por-transaccion.md.
    //  3. EVENTOS: se publican DESPUÉS de confirmar la transacción. Avisar de
    //     un pedido que luego no se guardó sería peor que no avisar.

    /// <summary>Ejecuta el caso de uso.</summary>
    public async Task<Result<OrderDto>> ExecuteAsync(
        PlaceOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var currency = Currency.Create(command.Currency);
        var now = timeProvider.GetUtcNow();

        // Si el mismo producto llega en varias líneas, se suman aquí: así se
        // carga UNA sola vez cada producto. Cargarlo dos veces daría dos objetos
        // distintos en memoria y el segundo guardado pisaría al primero.
        var requestedQuantities = command.Lines
            .GroupBy(line => line.ProductId)
            .ToDictionary(group => new ProductId(group.Key), group => group.Sum(line => line.Quantity));

        await unitOfWork.BeginTransactionAsync(cancellationToken);

        // 1. Cargar los productos pedidos.
        var products = new Dictionary<ProductId, Product>();

        foreach (var productId in requestedQuantities.Keys)
        {
            var product = await productRepository.GetByIdAsync(productId, cancellationToken);

            if (product is null)
            {
                return Result.Failure<OrderDto>(OrderUseCaseErrors.ProductNotFound(productId.Value));
            }

            products.Add(productId, product);
        }

        // 2. SERVICIO DE DOMINIO: ¿alcanza el stock para todo el pedido? Se
        //    pregunta por todo junto para poder responder con la lista completa
        //    de faltantes, en lugar de fallar en el primero.
        var shortages = StockAvailability.FindShortages(
            requestedQuantities.Select(entry => new StockRequest(products[entry.Key], entry.Value)));

        if (shortages.Count > 0)
        {
            return Result.Failure<OrderDto>(OrderUseCaseErrors.InsufficientStock(shortages));
        }

        // 3. Armar el pedido y descontar el stock. El precio unitario se COPIA
        //    del producto: si mañana cambia, este pedido no cambia.
        var order = Order.Create(currency, now);

        foreach (var (productId, quantity) in requestedQuantities)
        {
            var product = products[productId];

            order.AddLine(productId, product.Price, quantity);
            product.RemoveStock(quantity, now);
        }

        order.Place(now);

        // 4. Guardar todo dentro de la MISMA transacción.
        await orderRepository.AddAsync(order, cancellationToken);

        foreach (var product in products.Values)
        {
            if (!await productRepository.UpdateAsync(product, cancellationToken))
            {
                // Alguien cambió ese producto entre que lo leímos y lo
                // guardamos. Al no confirmar la transacción, no queda nada a
                // medias: ni pedido ni stock descontado.
                return Result.Failure<OrderDto>(OrderUseCaseErrors.ConcurrencyConflict(order.Id.Value));
            }
        }

        await unitOfWork.CommitAsync(cancellationToken);

        // 5. Ahora que los datos están a salvo, publicar lo que ocurrió.
        await domainEventDispatcher.DispatchAsync(order.PullDomainEvents(), cancellationToken);

        return OrderDto.FromOrder(order);
    }
}
