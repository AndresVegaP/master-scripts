using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Application.Products;
using CleanArchitecture.Application.Products.DeleteProduct;
using CleanArchitecture.Application.Products.GetProductById;
using CleanArchitecture.Application.Products.ListProducts;
using CleanArchitecture.UnitTests.Fakes;

namespace CleanArchitecture.UnitTests.Application.Products;

/// <summary>Tests de los casos de uso de consulta y baja de productos.</summary>
public class ProductQueryHandlersTests
{
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryOrderRepository _orders = new();

    [Fact]
    public async Task GetProductById_ConProductoExistente_DevuelveElProducto()
    {
        var product = Some.Product();
        _products.Seed(product);

        var result = await new GetProductByIdHandler(_products)
            .ExecuteAsync(new GetProductByIdQuery(product.Id.Value));

        Assert.True(result.IsSuccess);
        Assert.Equal(product.Id.Value, result.Value.Id);
        Assert.Equal("TEC-0001", result.Value.Sku);
    }

    [Fact]
    public async Task GetProductById_ConProductoInexistente_DevuelveNotFound()
    {
        var result = await new GetProductByIdHandler(_products)
            .ExecuteAsync(new GetProductByIdQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task DeleteProduct_ConProductoSinPedidos_LoElimina()
    {
        var product = Some.Product();
        _products.Seed(product);

        var result = await new DeleteProductHandler(_products, _orders)
            .ExecuteAsync(new DeleteProductCommand(product.Id.Value));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _products.Count);
    }

    [Fact]
    public async Task DeleteProduct_ConProductoInexistente_DevuelveNotFound()
    {
        var result = await new DeleteProductHandler(_products, _orders)
            .ExecuteAsync(new DeleteProductCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Product.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task DeleteProduct_ConProductoUsadoEnPedidos_DevuelveConflictoYNoLoElimina()
    {
        // Regla ENTRE AGREGADOS: la coordina el caso de uso, porque ni Product ni
        // Order pueden verla solos.
        var product = Some.Product();
        _products.Seed(product);
        _orders.Seed(Some.PlacedOrder(product.Id));

        var result = await new DeleteProductHandler(_products, _orders)
            .ExecuteAsync(new DeleteProductCommand(product.Id.Value));

        Assert.True(result.IsFailure);
        Assert.Equal("Product.UsedInOrders", result.Error.Code);
        Assert.Equal(1, _products.Count);
    }

    [Theory]
    [InlineData(0, 20, 1, 20)]      // la página 0 no existe: se corrige a 1
    [InlineData(-5, 20, 1, 20)]
    [InlineData(2, 999, 2, 100)]    // nadie se lleva el catálogo entero
    [InlineData(3, 0, 3, 1)]
    [InlineData(2, 50, 2, 50)]      // valores razonables: se respetan
    public async Task ListProducts_NormalizaLaPaginacion(
        int requestedPage,
        int requestedPageSize,
        int expectedPage,
        int expectedPageSize)
    {
        var queries = new StubProductQueries(new PagedResult<ProductDto>([], 1, 20, 0));

        await new ListProductsHandler(queries).ExecuteAsync(new ListProductsQuery(requestedPage, requestedPageSize));

        Assert.Equal(expectedPage, queries.LastPage);
        Assert.Equal(expectedPageSize, queries.LastPageSize);
    }

    [Fact]
    public async Task ListProducts_DevuelveLaPaginaDelPuertoDeLectura()
    {
        var expected = new PagedResult<ProductDto>([], 1, 20, 0);
        var queries = new StubProductQueries(expected);

        var result = await new ListProductsHandler(queries).ExecuteAsync(new ListProductsQuery());

        Assert.True(result.IsSuccess);
        Assert.Same(expected, result.Value);
    }
}
