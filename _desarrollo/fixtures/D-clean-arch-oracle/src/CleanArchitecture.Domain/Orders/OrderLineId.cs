namespace CleanArchitecture.Domain.Orders;

/// <summary>Identidad de una línea de pedido.</summary>
/// <param name="Value">Valor subyacente del identificador.</param>
public readonly record struct OrderLineId(Guid Value)
{
    // 📘 docs/05-agregados.md
    //
    // La línea de pedido es una ENTIDAD HIJA: tiene identidad, pero solo dentro
    // de su pedido. Nadie la busca por su Id en toda la aplicación; se llega a
    // ella a través del pedido (la raíz del agregado). Por eso no existe un
    // IOrderLineRepository: sería una puerta trasera al interior del agregado.

    /// <summary>Genera una identidad nueva, ordenable en el tiempo (UUID versión 7).</summary>
    public static OrderLineId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
