namespace CleanArchitecture.Domain.Common;

/// <summary>
/// Clase base de las entidades: objetos que se distinguen por su identidad
/// (<see cref="Id"/>) y no por el valor de sus datos.
/// </summary>
/// <typeparam name="TId">
/// Tipo del identificador. Usamos Ids fuertemente tipados (por ejemplo
/// <c>ProductId</c>) en lugar de <see cref="Guid"/> sueltos.
/// </typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : struct, IEquatable<TId>
{
    // 📘 docs/04-entidades.md
    //
    // ¿ENTIDAD O VALUE OBJECT? Hazte una sola pregunta:
    //   "¿Me importa CUÁL es, o solo QUÉ VALOR tiene?"
    //   - Dos pedidos con los mismos productos siguen siendo DOS pedidos → entidad.
    //   - Dos billetes de 10 USD son intercambiables                    → value object.
    //
    // Esta clase centraliza la igualdad por identidad para no repetirla en
    // cada entidad del dominio.

    protected Entity(TId id)
    {
        Id = id;
    }

    /// <summary>Identidad de la entidad. No cambia nunca durante su vida.</summary>
    public TId Id { get; }

    public bool Equals(Entity<TId>? other) =>
        other is not null
        && other.GetType() == GetType() // un Product y un Order nunca son "iguales", aunque coincida el Guid
        && Id.Equals(other.Id);

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    // Regla de .NET: si sobrescribes Equals, sobrescribe también GetHashCode
    // con los mismos datos. Si no, HashSet y Dictionary se comportan mal.
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
