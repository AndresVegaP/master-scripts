using CleanArchitecture.Domain.Products;
using CleanArchitecture.Domain.SharedKernel;
using CleanArchitecture.Infrastructure.Persistence;
using CleanArchitecture.Infrastructure.Persistence.Products;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleanArchitecture.IntegrationTests.Persistence;

/// <summary>Tests del lado de LECTURA (CQRS) y del migrador de esquema.</summary>
public class ProductQueriesAndMigrationsTests : SqliteTestDatabase
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private async Task SeedProductsAsync(int count)
    {
        var repository = new ProductRepository(Session);

        for (var i = 1; i <= count; i++)
        {
            var product = Product.Create(
                Sku.Create($"TEC-{i:0000}"),
                ProductName.Create($"Producto {i}"),
                Money.Create(1000m * i, "CLP"),
                initialStock: i,
                // Fechas distintas para poder comprobar el orden del listado.
                Now.AddMinutes(i));

            await repository.AddAsync(product, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ListAsync_DevuelveLaPaginaPedidaOrdenadaPorFecha()
    {
        await SeedProductsAsync(5);
        var queries = new ProductQueries(Session);

        var page = await queries.ListAsync(1, 2, TestContext.Current.CancellationToken);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasNextPage);
        Assert.Equal(["TEC-0001", "TEC-0002"], page.Items.Select(item => item.Sku));
    }

    [Fact]
    public async Task ListAsync_EnLaUltimaPagina_DevuelveElResto()
    {
        await SeedProductsAsync(5);
        var queries = new ProductQueries(Session);

        var page = await queries.ListAsync(3, 2, TestContext.Current.CancellationToken);

        Assert.Single(page.Items);
        Assert.Equal("TEC-0005", page.Items[0].Sku);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task ListAsync_SinProductos_DevuelveUnaPaginaVacia()
    {
        var queries = new ProductQueries(Session);

        var page = await queries.ListAsync(1, 20, TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task ListAsync_TraeLosMismosDatosQueElRepositorio()
    {
        // El lado de lectura toma un atajo (SQL directo al DTO, sin construir el
        // agregado). Este test comprueba que ese atajo no cambia lo que se ve.
        await SeedProductsAsync(1);

        var fromQueries = (await new ProductQueries(Session).ListAsync(1, 10, TestContext.Current.CancellationToken)).Items[0];
        var fromRepository = await new ProductRepository(Session)
            .GetByIdAsync(new ProductId(fromQueries.Id), TestContext.Current.CancellationToken);

        Assert.Equal(fromRepository!.Sku.Value, fromQueries.Sku);
        Assert.Equal(fromRepository.Price.Amount, fromQueries.Price);
        Assert.Equal(fromRepository.CreatedAt, fromQueries.CreatedAt);
    }

    [Fact]
    public async Task MigrateAsync_EsIdempotente()
    {
        // Arrancar la aplicación dos veces no debe volver a aplicar lo aplicado.
        // (La base de este test ya fue migrada por SqliteTestDatabase.)
        var migrator = new DatabaseMigrator(ConnectionFactory, NullLogger<DatabaseMigrator>.Instance);

        await migrator.MigrateAsync(TestContext.Current.CancellationToken);
        await migrator.MigrateAsync(TestContext.Current.CancellationToken);

        await using var connection = await ConnectionFactory.CreateOpenConnectionAsync(TestContext.Current.CancellationToken);
        var applied = await connection.QueryAsync<long>(new CommandDefinition(
            "SELECT Version FROM SchemaVersions ORDER BY Version",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal([1L, 2L], applied);
    }

    [Fact]
    public async Task ElEsquema_ProtegeLasReglasCriticas()
    {
        // La base de datos es la última línea de defensa: aunque alguien la
        // edite a mano, no debe poder dejar un stock negativo.
        await using var connection = await ConnectionFactory.CreateOpenConnectionAsync(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO Products (Id, Sku, Name, PriceAmount, PriceCurrency, Stock, CreatedAt, UpdatedAt, Version)
                VALUES ('x', 'TEC-0001', 'Producto', '1000', 'CLP', -5, '2026-01-01T00:00:00+00:00', NULL, 1)
                """,
                cancellationToken: TestContext.Current.CancellationToken)));

        Assert.Contains("CHECK", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
