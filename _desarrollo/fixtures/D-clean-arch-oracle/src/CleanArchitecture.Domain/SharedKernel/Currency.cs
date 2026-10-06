using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.SharedKernel;

/// <summary>
/// Moneda según ISO 4217 (por ejemplo, USD), con la cantidad de decimales que admite.
/// </summary>
public sealed class Currency : ValueObject
{
    // 📘 docs/03-value-objects.md
    //
    // Un VO puede llevar DATOS además de validación. ISO 4217 define para cada
    // moneda sus "unidades menores": el dólar usa 2 decimales (centavos) y el
    // peso chileno 0. Money le pregunta a Currency cuántos decimales admite, en
    // lugar de tener un 2 mágico repartido por el código.
    //
    // En un sistema real esta tabla vendría de configuración o de una tabla de
    // la base de datos; para aprender, una lista fija es perfecta.
    private static readonly Dictionary<string, int> SupportedCurrencies = new(StringComparer.Ordinal)
    {
        ["USD"] = 2,
        ["EUR"] = 2,
        ["MXN"] = 2,
        ["COP"] = 2,
        ["PEN"] = 2,
        ["CLP"] = 0,
    };

    private Currency(string code, int decimalPlaces)
    {
        Code = code;
        DecimalPlaces = decimalPlaces;
    }

    /// <summary>Código ISO 4217 en mayúsculas.</summary>
    public string Code { get; }

    /// <summary>Cantidad máxima de decimales que admite un importe en esta moneda.</summary>
    public int DecimalPlaces { get; }

    /// <summary>Monedas que este dominio acepta hoy.</summary>
    public static IReadOnlyCollection<string> SupportedCodes => SupportedCurrencies.Keys;

    /// <summary>Crea la moneda a partir de su código. Acepta minúsculas y espacios alrededor.</summary>
    /// <exception cref="DomainException">Si el código está vacío o la moneda no está soportada.</exception>
    public static Currency Create(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException(CurrencyErrors.Required);
        }

        // Tolerante al leer, estricto al guardar: aceptamos "  usd " y lo
        // normalizamos a "USD".
        var normalized = code.Trim().ToUpperInvariant();

        if (!SupportedCurrencies.TryGetValue(normalized, out var decimalPlaces))
        {
            throw new DomainException(CurrencyErrors.NotSupported(code, SupportedCurrencies.Keys));
        }

        return new Currency(normalized, decimalPlaces);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Code;
    }

    public override string ToString() => Code;
}
