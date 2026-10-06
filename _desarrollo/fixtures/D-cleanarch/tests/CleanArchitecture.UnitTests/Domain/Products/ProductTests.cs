using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Products;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.UnitTests.Domain.Products;

/// <summary>Tests del agregado Product: sus invariantes y su snapshot.</summary>
public class ProductTests
{
    // Fíjate en lo baratos que son estos tests: sin base de datos, sin dobles,
    // sin arrancar nada. Crear el objeto, llamar a un método y verificar. Esa es
    // la recompensa de un dominio sin dependencias: la lógica más importante del
    // sistema se prueba en milisegundos.

    [Fact]
    public void Create_ConDatosValidos_InicializaElProducto()
    {
        var product = Some.Product(stock: 10);

        Assert.NotEqual(default, product.Id);
        Assert.Equal("TEC-0001", product.Sku.Value);
        Assert.Equal(10, product.Stock);
        Assert.Equal(Some.Now, product.CreatedAt);
        Assert.Null(product.UpdatedAt); // recién creado: nunca modificado
        Assert.Equal(1, product.Version);
    }

    [Fact]
    public void Create_ConStockNegativo_LanzaDomainException()
    {
        var exception = Assert.Throws<DomainException>(() => Some.Product(stock: -1));

        Assert.Equal("Product.NegativeStock", exception.Code);
    }

    [Fact]
    public void ChangePrice_ConLaMismaMoneda_ActualizaPrecioYFecha()
    {
        var product = Some.Product();
        var later = Some.Now.AddDays(1);

        product.ChangePrice(Some.Money(79990m), later);

        Assert.Equal(79990m, product.Price.Amount);
        Assert.Equal(later, product.UpdatedAt);
    }

    [Fact]
    public void ChangePrice_ConOtraMoneda_LanzaDomainExceptionDeConflicto()
    {
        var product = Some.Product(); // precio en CLP

        var exception = Assert.Throws<DomainException>(
            () => product.ChangePrice(Money.Create(99.99m, "USD"), Some.Now));

        Assert.Equal("Product.CurrencyChangeNotAllowed", exception.Code);
        Assert.Equal(DomainErrorKind.Conflict, exception.Kind);
    }

    [Fact]
    public void Rename_CambiaElNombreYRegistraLaFecha()
    {
        var product = Some.Product();

        product.Rename(ProductName.Create("Teclado inalámbrico"), Some.Now.AddHours(2));

        Assert.Equal("Teclado inalámbrico", product.Name.Value);
        Assert.NotNull(product.UpdatedAt);
    }

    [Fact]
    public void AddStock_ConCantidadValida_SumaUnidades()
    {
        var product = Some.Product(stock: 10);

        product.AddStock(5, Some.Now);

        Assert.Equal(15, product.Stock);
    }

    [Fact]
    public void RemoveStock_ConStockSuficiente_DescuentaUnidades()
    {
        var product = Some.Product(stock: 10);

        product.RemoveStock(4, Some.Now);

        Assert.Equal(6, product.Stock);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void AddStock_ConCantidadNoPositiva_LanzaDomainException(int quantity)
    {
        var exception = Assert.Throws<DomainException>(() => Some.Product().AddStock(quantity, Some.Now));

        Assert.Equal("Product.NonPositiveQuantity", exception.Code);
    }

    [Fact]
    public void RemoveStock_ConMasUnidadesDeLasDisponibles_LanzaDomainExceptionYNoCambiaElEstado()
    {
        // LA invariante estrella de este agregado: el stock nunca baja de cero.
        var product = Some.Product(stock: 3);

        var exception = Assert.Throws<DomainException>(() => product.RemoveStock(5, Some.Now));

        Assert.Equal("Product.InsufficientStock", exception.Code);
        Assert.Equal(DomainErrorKind.Conflict, exception.Kind);
        Assert.Equal(3, product.Stock); // el estado NO quedó a medias
        Assert.Null(product.UpdatedAt);
    }

    [Fact]
    public void ToSnapshot_YFromSnapshot_ConservanTodoElEstado()
    {
        // El test de ida y vuelta del patrón Snapshot: lo que sale del agregado
        // tiene que poder volver a entrar sin perder nada.
        var original = Some.Product(stock: 7);
        original.ChangePrice(Some.Money(79990m), Some.Now.AddDays(1));

        var rehydrated = Product.FromSnapshot(original.ToSnapshot());

        Assert.Equal(original.Id, rehydrated.Id);
        Assert.Equal(original.Sku, rehydrated.Sku);
        Assert.Equal(original.Name, rehydrated.Name);
        Assert.Equal(original.Price, rehydrated.Price);
        Assert.Equal(original.Stock, rehydrated.Stock);
        Assert.Equal(original.CreatedAt, rehydrated.CreatedAt);
        Assert.Equal(original.UpdatedAt, rehydrated.UpdatedAt);
        Assert.Equal(original.Version, rehydrated.Version);

        // Y el snapshot del rehidratado es idéntico al del original: el ciclo
        // cierra. (Los snapshots son records: se comparan por valor.)
        Assert.Equal(original.ToSnapshot(), rehydrated.ToSnapshot());
    }

    [Fact]
    public void FromSnapshot_NoGeneraUnaIdentidadNueva()
    {
        // Diferencia clave entre Create (producto NUEVO) y FromSnapshot
        // (producto que YA EXISTE): el segundo conserva Id y fechas.
        var snapshot = Some.Product().ToSnapshot();

        Assert.Equal(snapshot.Id, Product.FromSnapshot(snapshot).Id.Value);
    }

    [Theory]
    [InlineData("sku-invalido", "CLP", 5)]
    [InlineData("TEC-0001", "XYZ", 5)]  // moneda que el dominio ya no acepta
    [InlineData("TEC-0001", "CLP", -3)] // stock imposible
    public void FromSnapshot_ConDatosCorruptos_LanzaCorruptedSnapshotException(
        string sku,
        string currency,
        int stock)
    {
        // Rehidratar NO es crear. Si un dato guardado ya no cumple las reglas de
        // hoy, la culpa es nuestra (falta una migración), no del cliente: por eso
        // NO sale una DomainException, que la API traduciría a un 4xx.
        var corrupted = Some.Product().ToSnapshot() with
        {
            Sku = sku,
            PriceCurrency = currency,
            Stock = stock,
        };

        var exception = Assert.Throws<CorruptedSnapshotException>(() => Product.FromSnapshot(corrupted));

        Assert.Equal(nameof(Product), exception.AggregateName);
        Assert.Equal(corrupted.Id, exception.AggregateId);
        Assert.IsType<DomainException>(exception.InnerException);
    }
}
