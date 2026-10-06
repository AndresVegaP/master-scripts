using CleanArchitecture.Domain.Products;
using CleanArchitecture.Domain.SharedKernel;
using CleanArchitecture.Infrastructure.Persistence.Products;

namespace CleanArchitecture.IntegrationTests.Persistence;

/// <summary>Tests del repositorio de productos contra SQLite real.</summary>
public class ProductRepositoryTests : SqliteTestDatabase
{
    // Lo que estos tests verifican y los unitarios NO pueden verificar:
    //   - que el SQL es válido y las columnas existen;
    //   - que el viaje de ida y vuelta no pierde datos (Guid, decimal, fechas);
    //   - que las restricciones de la tabla (UNIQUE, CHECK) existen de verdad;
    //   - que la concurrencia optimista funciona con la base de datos real.

    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private static Product NewProduct(string sku = "TEC-0001", decimal price = 89990m, string currency = "CLP", int stock = 10) =>
        Product.Create(
            Sku.Create(sku),
            ProductName.Create("Teclado mecánico"),
            Money.Create(price, currency),
            stock,
            Now);

    [Fact]
    public async Task AddAsync_YGetByIdAsync_ConservanTodoElEstado()
    {
        var repository = new ProductRepository(Session);
        var product = NewProduct();

        Assert.True(await repository.AddAsync(product, TestContext.Current.CancellationToken));
        var loaded = await repository.GetByIdAsync(product.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        // Comparamos los SNAPSHOTS: una sola línea verifica todos los campos, y
        // si mañana el agregado gana uno nuevo, este test lo cubre solo.
        Assert.Equal(product.ToSnapshot(), loaded.ToSnapshot());
    }

    [Fact]
    public async Task AddAsync_ConservaLaFechaConSuDesplazamiento()
    {
        // Las fechas son el clásico dato que se corrompe al guardar: se pierde
        // la zona horaria o los microsegundos. Por eso tiene su propio test.
        var repository = new ProductRepository(Session);
        var product = NewProduct();

        await repository.AddAsync(product, TestContext.Current.CancellationToken);
        var loaded = await repository.GetByIdAsync(product.Id, TestContext.Current.CancellationToken);

        Assert.Equal(Now, loaded!.CreatedAt);
        Assert.Equal(TimeSpan.Zero, loaded.CreatedAt.Offset);
        Assert.Null(loaded.UpdatedAt);
    }

    [Fact]
    public async Task AddAsync_ConPreciosDeCualquierMoneda_NoPierdePrecision()
    {
        // El dinero se guarda como TEXT justamente para esto: con REAL (coma
        // flotante binaria) 10.10 puede volver como 10.099999999999999.
        var repository = new ProductRepository(Session);
        var product = NewProduct(sku: "USD-0001", price: 10.10m, currency: "USD");

        await repository.AddAsync(product, TestContext.Current.CancellationToken);
        var loaded = await repository.GetByIdAsync(product.Id, TestContext.Current.CancellationToken);

        Assert.Equal(10.10m, loaded!.Price.Amount);
        Assert.Equal("USD", loaded.Price.Currency.Code);
    }

    [Fact]
    public async Task GetByIdAsync_ConIdInexistente_DevuelveNull()
    {
        var repository = new ProductRepository(Session);

        Assert.Null(await repository.GetByIdAsync(ProductId.New(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddAsync_ConSkuDuplicado_DevuelveFalse()
    {
        // DEFENSA EN PROFUNDIDAD: el caso de uso ya comprueba el SKU, pero dos
        // peticiones simultáneas pueden pasar ambas esa comprobación. La
        // restricción UNIQUE de la tabla es la última línea de defensa, y el
        // repositorio la traduce a "false" para que la API responda 409 y no 500.
        var repository = new ProductRepository(Session);
        await repository.AddAsync(NewProduct("TEC-0001"), TestContext.Current.CancellationToken);

        var added = await repository.AddAsync(NewProduct("TEC-0001"), TestContext.Current.CancellationToken);

        Assert.False(added);
    }

    [Fact]
    public async Task ExistsWithSkuAsync_DistingueExistenteDeInexistente()
    {
        var repository = new ProductRepository(Session);
        await repository.AddAsync(NewProduct("TEC-0001"), TestContext.Current.CancellationToken);

        Assert.True(await repository.ExistsWithSkuAsync(Sku.Create("TEC-0001"), TestContext.Current.CancellationToken));
        Assert.False(await repository.ExistsWithSkuAsync(Sku.Create("ZZZ-9999"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateAsync_GuardaLosCambiosYSubeLaVersion()
    {
        var repository = new ProductRepository(Session);
        var product = NewProduct();
        await repository.AddAsync(product, TestContext.Current.CancellationToken);

        product.ChangePrice(Money.Create(79990m, "CLP"), Now.AddDays(1));
        product.RemoveStock(3, Now.AddDays(1));

        Assert.True(await repository.UpdateAsync(product, TestContext.Current.CancellationToken));

        var loaded = await repository.GetByIdAsync(product.Id, TestContext.Current.CancellationToken);
        Assert.Equal(79990m, loaded!.Price.Amount);
        Assert.Equal(7, loaded.Stock);
        Assert.Equal(Now.AddDays(1), loaded.UpdatedAt);
        Assert.Equal(product.Version + 1, loaded.Version); // la versión la sube el UPDATE
    }

    [Fact]
    public async Task UpdateAsync_ConUnaVersionYaSuperada_DevuelveFalse()
    {
        // ESTE ES EL TEST DE LA ACTUALIZACIÓN PERDIDA. Dos "usuarios" cargan el
        // mismo producto; el primero guarda y el segundo trabaja sobre una
        // versión vieja. Sin control de concurrencia, el segundo pisaría el
        // cambio del primero en silencio.
        var repository = new ProductRepository(Session);
        var product = NewProduct();
        await repository.AddAsync(product, TestContext.Current.CancellationToken);

        var usuarioA = await repository.GetByIdAsync(product.Id, TestContext.Current.CancellationToken);
        var usuarioB = await repository.GetByIdAsync(product.Id, TestContext.Current.CancellationToken);

        usuarioA!.ChangePrice(Money.Create(70000m, "CLP"), Now);
        Assert.True(await repository.UpdateAsync(usuarioA, TestContext.Current.CancellationToken));

        usuarioB!.ChangePrice(Money.Create(60000m, "CLP"), Now);
        Assert.False(await repository.UpdateAsync(usuarioB, TestContext.Current.CancellationToken));

        // Gana el primero: el cambio del segundo NO se aplicó.
        var loaded = await repository.GetByIdAsync(product.Id, TestContext.Current.CancellationToken);
        Assert.Equal(70000m, loaded!.Price.Amount);
    }

    [Fact]
    public async Task DeleteAsync_DevuelveSiBorroAlgo()
    {
        var repository = new ProductRepository(Session);
        var product = NewProduct();
        await repository.AddAsync(product, TestContext.Current.CancellationToken);

        Assert.True(await repository.DeleteAsync(product.Id, TestContext.Current.CancellationToken));
        Assert.False(await repository.DeleteAsync(product.Id, TestContext.Current.CancellationToken));
        Assert.Null(await repository.GetByIdAsync(product.Id, TestContext.Current.CancellationToken));
    }
}
