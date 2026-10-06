using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.SharedKernel;

/// <summary>Catálogo de errores del value object <see cref="Money"/>.</summary>
public static class MoneyErrors
{
    public static DomainError NegativeAmount(decimal received) =>
        new(
            "Money.NegativeAmount",
            $"El importe no puede ser negativo (recibido: {received}).",
            DomainErrorKind.Invariant);

    public static DomainError TooManyDecimals(decimal received, Currency currency) =>
        new(
            "Money.TooManyDecimals",
            $"Un importe en {currency.Code} admite como máximo {currency.DecimalPlaces} decimales (recibido: {received}).",
            DomainErrorKind.Invariant);

    public static DomainError CurrencyMismatch(Currency left, Currency right) =>
        new(
            "Money.CurrencyMismatch",
            $"No se pueden operar importes de monedas distintas ({left} y {right}).",
            DomainErrorKind.Conflict);

    public static DomainError NegativeQuantity(int received) =>
        new(
            "Money.NegativeQuantity",
            $"No se puede multiplicar un importe por una cantidad negativa (recibido: {received}).",
            DomainErrorKind.Invariant);
}
