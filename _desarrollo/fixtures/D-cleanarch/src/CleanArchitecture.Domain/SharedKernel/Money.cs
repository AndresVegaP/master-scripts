using System.Globalization;
using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.SharedKernel;

/// <summary>Una cantidad de dinero: importe no negativo más su moneda.</summary>
public sealed class Money : ValueObject
{
    // 📘 docs/03-value-objects.md
    //
    //     decimal precio = 10.50m;   // ¿10.50 qué? ¿dólares? ¿pesos?
    //
    // Un número solo no significa nada: importe y moneda viajan JUNTOS.
    // Además, Money impide operaciones sin sentido, como sumar dólares con pesos.
    //
    // ¿Por qué decimal y no double? double es binario y no representa bien
    // muchos decimales (0.1 + 0.2 no da 0.3). Para dinero: SIEMPRE decimal.
    //
    // DECISIÓN CONSCIENTE: en este dominio Money nunca es negativo, porque no
    // hay descuentos ni reembolsos. Si los hubiera, Money admitiría negativos y
    // la regla "el precio no puede ser negativo" viviría en Product.
    // Ver docs/adr/0004-money-no-negativo.md.
    //
    // ¿Por qué vive en SharedKernel y no en Products? Porque lo usan varios
    // agregados (Product y Order). El núcleo compartido guarda los conceptos
    // que todo el dominio comparte y que casi nunca cambian.

    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    /// <summary>Crea un importe validando el signo y los decimales según la moneda.</summary>
    /// <exception cref="DomainException">
    /// Si el importe es negativo o tiene más decimales de los que admite la moneda.
    /// </exception>
    public static Money Create(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (amount < 0)
        {
            throw new DomainException(MoneyErrors.NegativeAmount(amount));
        }

        // No redondeamos en silencio: si llega 10.999 en una moneda de 2
        // decimales es un error de cálculo aguas arriba, y hay que verlo.
        // decimal.Round(10.999m, 2) da 11.00m, distinto de 10.999m.
        if (decimal.Round(amount, currency.DecimalPlaces) != amount)
        {
            throw new DomainException(MoneyErrors.TooManyDecimals(amount, currency));
        }

        // Normalizamos la ESCALA del decimal. En C#, 79990 y 79990.0 son el
        // mismo número (son iguales y tienen el mismo hash), pero se escriben
        // distinto en JSON y en la base de datos. Como el proveedor de SQLite
        // guarda los decimales con al menos un decimal, sin esta línea un precio
        // en pesos volvería de la base como "79990.0" y así saldría en la API.
        return new Money(decimal.Round(amount, currency.DecimalPlaces), currency);
    }

    /// <summary>Atajo: crea la moneda a partir de su código y después el importe.</summary>
    public static Money Create(decimal amount, string? currencyCode) =>
        Create(amount, Currency.Create(currencyCode));

    /// <summary>Cero en la moneda indicada. Útil como punto de partida para sumar.</summary>
    public static Money Zero(Currency currency) => Create(0m, currency);

    /// <summary>Suma dos importes de la misma moneda y devuelve un Money NUEVO (inmutabilidad).</summary>
    /// <exception cref="DomainException">Si las monedas son distintas.</exception>
    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureSameCurrency(other);

        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>Multiplica el importe por una cantidad de unidades (precio por cantidad).</summary>
    /// <exception cref="DomainException">Si la cantidad es negativa.</exception>
    public Money Multiply(int quantity)
    {
        if (quantity < 0)
        {
            throw new DomainException(MoneyErrors.NegativeQuantity(quantity));
        }

        return new Money(Amount * quantity, Currency);
    }

    private void EnsureSameCurrency(Money other)
    {
        // ¿Cuánto es 5 USD + 3 EUR? Depende del tipo de cambio, y eso NO es
        // responsabilidad de este VO. Por eso se prohíbe.
        if (Currency != other.Currency)
        {
            throw new DomainException(MoneyErrors.CurrencyMismatch(Currency, other.Currency));
        }
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() =>
        $"{Amount.ToString($"F{Currency.DecimalPlaces}", CultureInfo.InvariantCulture)} {Currency.Code}";
}
