using System.Text.RegularExpressions;
using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.Products;

/// <summary>
/// SKU (Stock Keeping Unit): el código con el que el negocio identifica cada
/// artículo. Formato de este dominio: 3 letras, guion y 4 dígitos (TEC-0001).
/// </summary>
public sealed partial class Sku : ValueObject
{
    // 📘 docs/03-value-objects.md
    //
    // Este VO muestra dos cosas que ProductName no tiene:
    //   1. Validación con expresión regular generada en tiempo de compilación.
    //   2. Normalización más agresiva: aceptamos "tec-0001" y guardamos
    //      "TEC-0001" (tolerante al leer, estricto al guardar).
    //
    // "partial" es necesario para que el generador de código de [GeneratedRegex]
    // pueda inyectar la implementación del método SkuFormat.

    private Sku(string value)
    {
        Value = value;
    }

    /// <summary>Valor normalizado del SKU.</summary>
    public string Value { get; }

    /// <summary>Crea un SKU validando su formato.</summary>
    /// <exception cref="DomainException">Si está vacío o no cumple el formato.</exception>
    public static Sku Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(ProductErrors.SkuRequired);
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (!SkuFormat().IsMatch(normalized))
        {
            throw new DomainException(ProductErrors.SkuInvalidFormat(value));
        }

        return new Sku(normalized);
    }

    /// <summary>
    /// [GeneratedRegex] es la forma moderna de declarar una expresión regular:
    /// el compilador genera el código que la evalúa, en vez de interpretarla en
    /// ejecución. Resultado: más rápido y con los errores de sintaxis detectados
    /// al compilar.
    ///
    /// Patrón ^[A-Z]{3}-\d{4}$
    ///   ^        inicio de la cadena (nada antes)
    ///   [A-Z]{3} exactamente 3 letras mayúsculas
    ///   -        un guion literal
    ///   \d{4}    exactamente 4 dígitos
    ///   $        fin de la cadena (nada después)
    /// </summary>
    [GeneratedRegex(@"^[A-Z]{3}-\d{4}$")]
    private static partial Regex SkuFormat();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
