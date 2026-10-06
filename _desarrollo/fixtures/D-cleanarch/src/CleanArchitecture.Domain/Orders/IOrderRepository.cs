using CleanArchitecture.Domain.Products;

namespace CleanArchitecture.Domain.Orders;

/// <summary>Puerto de persistencia del agregado Order.</summary>
public interface IOrderRepository
{
    // 📘 docs/10-transacciones-y-concurrencia.md
    //
    // Un repositorio POR AGREGADO RAÍZ, nunca por tabla. Este puerto guarda y
    // carga el pedido COMPLETO (con sus líneas): aunque por debajo sean dos
    // tablas, para el dominio es una sola cosa que se guarda de una vez, dentro
    // de la misma transacción.

    /// <summary>Busca un pedido con todas sus líneas. Null si no existe.</summary>
    Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default);

    /// <summary>Guarda un pedido nuevo junto con sus líneas.</summary>
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Guarda los cambios de un pedido existente.</summary>
    /// <returns>false si el pedido ya no existe o si alguien lo modificó antes (versión distinta).</returns>
    Task<bool> UpdateAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indica si algún pedido incluye ese producto. Lo usa el caso de uso de
    /// baja de productos: no se borra un producto que aparece en pedidos, para
    /// no dejar líneas apuntando a un producto inexistente.
    /// </summary>
    Task<bool> ExistsWithProductAsync(ProductId productId, CancellationToken cancellationToken = default);
}
