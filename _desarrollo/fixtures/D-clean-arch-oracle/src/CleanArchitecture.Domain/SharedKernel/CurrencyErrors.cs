using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.SharedKernel;

/// <summary>Catálogo de errores del value object <see cref="Currency"/>.</summary>
public static class CurrencyErrors
{
    public static DomainError Required =>
        new("Currency.Required", "La moneda es obligatoria (por ejemplo, USD).", DomainErrorKind.Invariant);

    public static DomainError NotSupported(string code, IEnumerable<string> supported) =>
        new(
            "Currency.NotSupported",
            $"La moneda {code} no está soportada. Monedas válidas: {string.Join(", ", supported)}.",
            DomainErrorKind.Invariant);
}
