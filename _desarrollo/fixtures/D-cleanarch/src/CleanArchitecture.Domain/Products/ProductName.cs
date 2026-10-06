using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.Products;

/// <summary>Nombre visible de un producto.</summary>
public sealed class ProductName : ValueObject
{
    // 📘 docs/03-value-objects.md
    //
    // ¿Por qué no un simple string? Porque un string acepta cualquier cosa:
    // null, vacío, 5000 caracteres... Si usáramos string, cada método que
    // recibe un nombre tendría que repetir las mismas validaciones, y alguien
    // las olvidaría. Con este VO la regla vive en UN solo lugar: si tienes un
    // ProductName en la mano, ES válido. A esto se le llama
    // "parse, don't validate": conviertes el dato crudo en un tipo seguro una
    // vez, en la frontera, y a partir de ahí trabajas con el tipo.

    /// <summary>Longitud mínima permitida.</summary>
    public const int MinLength = 3;

    /// <summary>Longitud máxima permitida.</summary>
    public const int MaxLength = 100;

    private ProductName(string value)
    {
        Value = value;
    }

    /// <summary>Valor normalizado (sin espacios al inicio ni al final).</summary>
    public string Value { get; }

    /// <summary>Crea el nombre validando su longitud.</summary>
    /// <exception cref="DomainException">Si está vacío o su longitud está fuera de rango.</exception>
    public static ProductName Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(ProductErrors.NameRequired);
        }

        // Normalizar ANTES de medir: "  Teclado  " y "Teclado" son el mismo nombre.
        var normalized = value.Trim();

        if (normalized.Length is < MinLength or > MaxLength)
        {
            throw new DomainException(
                ProductErrors.NameInvalidLength(MinLength, MaxLength, normalized.Length));
        }

        return new ProductName(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
