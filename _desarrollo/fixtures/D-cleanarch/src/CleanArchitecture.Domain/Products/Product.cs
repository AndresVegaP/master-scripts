using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.Domain.Products;

/// <summary>Producto del catálogo. Es la raíz del agregado Product.</summary>
public sealed class Product : AggregateRoot<ProductId>
{
    // 📘 docs/05-agregados.md
    //
    // Esto es un MODELO RICO, y se nota en cuatro detalles:
    //
    //  1. Sus datos son VALUE OBJECTS (Sku, ProductName, Money), no primitivos
    //     sueltos: el tipo ya garantiza que el dato es válido.
    //  2. Todos los setters son privados. Nadie puede escribir product.Stock = -5.
    //     El estado solo cambia por métodos con NOMBRE DE NEGOCIO.
    //  3. El constructor es privado: un Product siempre nace válido, por la
    //     fábrica Create (producto nuevo) o por FromSnapshot (rehidratación).
    //  4. Las reglas viven aquí dentro, no en un "servicio" de fuera.
    //
    // Lo contrario —propiedades públicas con setters y la lógica repartida por
    // toda la aplicación— se llama MODELO ANÉMICO y es el antipatrón más común
    // del mundo real.

    private Product(
        ProductId id,
        Sku sku,
        ProductName name,
        Money price,
        int stock,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt,
        int version)
        : base(id, version)
    {
        Sku = sku;
        Name = name;
        Price = price;
        Stock = stock;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Código único de negocio. No cambia durante la vida del producto.</summary>
    public Sku Sku { get; }

    /// <summary>Nombre visible del producto.</summary>
    public ProductName Name { get; private set; }

    /// <summary>Precio de venta actual.</summary>
    public Money Price { get; private set; }

    /// <summary>
    /// Unidades disponibles. Es un int y no un value object a propósito: su
    /// única regla (nunca negativo) protege a ESTE agregado, no al número en sí.
    /// Crea un VO cuando el concepto tenga reglas o comportamiento propios.
    /// </summary>
    public int Stock { get; private set; }

    /// <summary>Momento de creación.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Última modificación. Null significa que nunca se modificó.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Crea un producto nuevo.</summary>
    /// <param name="sku">Código de negocio, único en el catálogo.</param>
    /// <param name="name">Nombre visible.</param>
    /// <param name="price">Precio inicial.</param>
    /// <param name="initialStock">Unidades iniciales, cero o más.</param>
    /// <param name="now">
    /// Momento actual. El dominio NO lee el reloj del sistema: recibe la hora
    /// desde fuera, para que los tests puedan congelarla (ver docs/09-tests.md).
    /// </param>
    /// <exception cref="DomainException">Si el stock inicial es negativo.</exception>
    public static Product Create(Sku sku, ProductName name, Money price, int initialStock, DateTimeOffset now)
    {
        if (initialStock < 0)
        {
            throw new DomainException(ProductErrors.NegativeStock(initialStock));
        }

        return new Product(
            ProductId.New(), // la identidad nace en el dominio, no en la base de datos
            sku,
            name,
            price,
            initialStock,
            createdAt: now,
            updatedAt: null,
            version: InitialVersion);
    }

    // ────────────────────────────────────────────────────────────────────
    // COMPORTAMIENTO DE NEGOCIO
    // Cada método público es una acción que el negocio reconoce con ese
    // nombre. Nada de SetPrice: el lenguaje del código es el del negocio
    // (lenguaje ubicuo).
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Cambia el nombre visible del producto.</summary>
    public void Rename(ProductName newName, DateTimeOffset now)
    {
        Name = newName;
        Touch(now);
    }

    /// <summary>Cambia el precio, manteniendo la moneda actual.</summary>
    /// <exception cref="DomainException">Si la moneda del nuevo precio es distinta.</exception>
    public void ChangePrice(Money newPrice, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(newPrice);

        // Cambiar la moneda de un producto sería otra operación de negocio,
        // con sus propias consecuencias, no un simple cambio de precio.
        if (newPrice.Currency != Price.Currency)
        {
            throw new DomainException(
                ProductErrors.CurrencyChangeNotAllowed(Price.Currency, newPrice.Currency));
        }

        Price = newPrice;
        Touch(now);
    }

    /// <summary>Suma unidades al stock (una reposición).</summary>
    public void AddStock(int quantity, DateTimeOffset now)
    {
        EnsurePositiveQuantity(quantity);

        Stock += quantity;
        Touch(now);
    }

    /// <summary>Descuenta unidades del stock (una venta).</summary>
    /// <exception cref="DomainException">Si no hay unidades suficientes.</exception>
    public void RemoveStock(int quantity, DateTimeOffset now)
    {
        EnsurePositiveQuantity(quantity);

        // LA invariante de este agregado: el stock nunca baja de cero.
        // Al vivir dentro de la entidad es imposible saltársela desde fuera.
        if (quantity > Stock)
        {
            throw new DomainException(ProductErrors.InsufficientStock(Sku, Stock, quantity));
        }

        Stock -= quantity;
        Touch(now);
    }

    /// <summary>Entrega una copia plana del estado actual, para guardarlo.</summary>
    public ProductSnapshot ToSnapshot() =>
        new(
            Id.Value,
            Sku.Value,
            Name.Value,
            Price.Amount,
            Price.Currency.Code,
            Stock,
            CreatedAt,
            UpdatedAt,
            Version);

    /// <summary>Reconstruye un producto que YA EXISTE a partir de su estado guardado.</summary>
    /// <exception cref="CorruptedSnapshotException">Si el estado guardado no cumple las reglas actuales.</exception>
    public static Product FromSnapshot(ProductSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            if (snapshot.Stock < 0)
            {
                throw new DomainException(ProductErrors.NegativeStock(snapshot.Stock));
            }

            return new Product(
                new ProductId(snapshot.Id),
                Sku.Create(snapshot.Sku),
                ProductName.Create(snapshot.Name),
                Money.Create(snapshot.PriceAmount, snapshot.PriceCurrency),
                snapshot.Stock,
                snapshot.CreatedAt,
                snapshot.UpdatedAt,
                snapshot.Version);
        }
        catch (DomainException exception)
        {
            // REHIDRATAR NO ES CREAR. Si un dato ya guardado no cumple las
            // reglas de HOY (por ejemplo, se dejó de aceptar una moneda), el
            // problema es nuestro: falta una migración de datos. Convertirlo en
            // un 4xx le diría al cliente que se equivocó él, y no es cierto.
            throw new CorruptedSnapshotException(nameof(Product), snapshot.Id, exception);
        }
    }

    /// <summary>Una sola validación de cantidad, usada por AddStock y RemoveStock.</summary>
    private static void EnsurePositiveQuantity(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException(ProductErrors.NonPositiveQuantity(quantity));
        }
    }

    /// <summary>Marca el producto como modificado. Un solo lugar, una sola forma.</summary>
    private void Touch(DateTimeOffset now) => UpdatedAt = now;
}
