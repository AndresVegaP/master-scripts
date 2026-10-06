namespace CleanArchitecture.Domain.Common;

/// <summary>
/// Clase base de los value objects: objetos inmutables, sin identidad, que
/// se comparan por su valor.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    // 📘 docs/03-value-objects.md
    //
    // Un VALUE OBJECT (VO):
    //   - no tiene Id: 10 USD es igual a 10 USD, sin importar la instancia;
    //   - es INMUTABLE: para "cambiarlo" se crea otro;
    //   - se VALIDA al crearse: si existe, es válido ("parse, don't validate").
    //
    // ¿Y los record de C#? También comparan por valor y son ideales para VOs
    // SIN reglas, como ProductId. Para VOs CON validación y comportamiento
    // (Money, Sku...) usamos esta clase: deja explícito qué datos forman el
    // valor (GetEqualityComponents) y evita que la expresión "with" de un
    // record con propiedades init cree copias saltándose la validación.

    /// <summary>Datos que definen el valor del objeto, en orden.</summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other) =>
        other is not null
        && other.GetType() == GetType()
        && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());

    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
