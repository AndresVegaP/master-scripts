namespace CleanArchitecture.Domain.Orders;

/// <summary>Estados por los que pasa un pedido.</summary>
public enum OrderStatus
{
    /// <summary>Se está armando: admite agregar y quitar líneas.</summary>
    Draft,

    /// <summary>Confirmado por el cliente: ya no admite cambios en sus líneas.</summary>
    Placed,

    /// <summary>Cancelado: se devolvieron las unidades al stock.</summary>
    Cancelled,
}
