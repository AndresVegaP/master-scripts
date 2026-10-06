using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.Domain.Orders;

/// <summary>Catálogo de errores del agregado Order.</summary>
public static class OrderErrors
{
    public static DomainError NotDraft(OrderStatus current) =>
        new(
            "Order.NotDraft",
            $"Solo se pueden modificar las líneas de un pedido en borrador (estado actual: {current}).",
            DomainErrorKind.Conflict);

    public static DomainError NotPlaced(OrderStatus current) =>
        new(
            "Order.NotPlaced",
            $"Solo se puede cancelar un pedido confirmado (estado actual: {current}).",
            DomainErrorKind.Conflict);

    public static DomainError NoLines =>
        new(
            "Order.NoLines",
            "No se puede confirmar un pedido sin líneas.",
            DomainErrorKind.Conflict);

    public static DomainError TooManyLines(int maxLines) =>
        new(
            "Order.TooManyLines",
            $"Un pedido admite como máximo {maxLines} productos distintos.",
            DomainErrorKind.Conflict);

    public static DomainError QuantityOutOfRange(int received, int maxQuantity) =>
        new(
            "Order.QuantityOutOfRange",
            $"La cantidad de una línea debe estar entre 1 y {maxQuantity} (recibido: {received}).",
            DomainErrorKind.Invariant);

    public static DomainError LineCurrencyMismatch(Currency orderCurrency, Currency lineCurrency) =>
        new(
            "Order.LineCurrencyMismatch",
            $"El pedido está en {orderCurrency} y la línea llega en {lineCurrency}.",
            DomainErrorKind.Invariant);

    public static DomainError LinePriceMismatch(Money existingPrice, Money newPrice) =>
        new(
            "Order.LinePriceMismatch",
            $"El producto ya está en el pedido con un precio unitario de {existingPrice} y ahora llega a {newPrice}.",
            DomainErrorKind.Conflict);
}
