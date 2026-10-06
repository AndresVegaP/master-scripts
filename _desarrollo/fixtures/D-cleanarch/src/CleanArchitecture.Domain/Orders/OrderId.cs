namespace CleanArchitecture.Domain.Orders;

/// <summary>Identidad de un pedido.</summary>
/// <param name="Value">Valor subyacente del identificador.</param>
public readonly record struct OrderId(Guid Value)
{
    /// <summary>Genera una identidad nueva, ordenable en el tiempo (UUID versión 7).</summary>
    public static OrderId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
