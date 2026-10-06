using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CleanArchitecture.IntegrationTests.Api;

/// <summary>Tests de extremo a extremo del recurso pedidos.</summary>
public class OrdersApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> CreateProductAsync(string sku, int stock)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/products",
            new { sku, name = "Producto de prueba", price = 1000m, currency = "CLP", initialStock = stock },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        return body.GetProperty("id").GetGuid();
    }

    private async Task<int> GetStockAsync(Guid productId)
    {
        var product = await _client.GetFromJsonAsync<JsonElement>($"/api/products/{productId}", TestContext.Current.CancellationToken);

        return product.GetProperty("stock").GetInt32();
    }

    [Fact]
    public async Task Post_ConStockSuficiente_ConfirmaElPedidoYDescuentaElStock()
    {
        // ESTE TEST RECORRE TODO EL REPO: endpoint, caso de uso, servicio de
        // dominio, dos agregados, una transacción y un evento de dominio.
        var productId = await CreateProductAsync("ORD-0001", stock: 10);

        var response = await _client.PostAsJsonAsync(
            "/api/orders",
            new { currency = "CLP", lines = new[] { new { productId, quantity = 3 } } },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var order = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Placed", order.GetProperty("status").GetString());
        Assert.Equal(3000m, order.GetProperty("total").GetDecimal()); // 1000 x 3
        Assert.Single(order.GetProperty("lines").EnumerateArray());

        Assert.Equal(7, await GetStockAsync(productId));
    }

    [Fact]
    public async Task Post_SinStockSuficiente_Devuelve409YNoTocaElStock()
    {
        var productId = await CreateProductAsync("ORD-0002", stock: 2);

        var response = await _client.PostAsJsonAsync(
            "/api/orders",
            new { currency = "CLP", lines = new[] { new { productId, quantity = 5 } } },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Order.InsufficientStock", problem.GetProperty("errorCode").GetString());

        // La transacción se deshizo: el stock quedó intacto.
        Assert.Equal(2, await GetStockAsync(productId));
    }

    [Fact]
    public async Task Post_ConProductoInexistente_Devuelve404()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/orders",
            new { currency = "CLP", lines = new[] { new { productId = Guid.NewGuid(), quantity = 1 } } },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Order.ProductNotFound", problem.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Post_SinLineas_Devuelve400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/orders",
            new { currency = "CLP", lines = Array.Empty<object>() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_DevuelveElStockYDejaElPedidoCancelado()
    {
        var productId = await CreateProductAsync("ORD-0003", stock: 5);

        var created = await _client.PostAsJsonAsync(
            "/api/orders",
            new { currency = "CLP", lines = new[] { new { productId, quantity = 4 } } },
            TestContext.Current.CancellationToken);
        var order = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var orderId = order.GetProperty("id").GetGuid();

        Assert.Equal(1, await GetStockAsync(productId));

        var cancelled = await _client.PostAsync($"/api/orders/{orderId}/cancel", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var cancelledOrder = await cancelled.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Cancelled", cancelledOrder.GetProperty("status").GetString());

        Assert.Equal(5, await GetStockAsync(productId)); // las unidades volvieron
    }

    [Fact]
    public async Task Cancel_DosVeces_Devuelve409()
    {
        var productId = await CreateProductAsync("ORD-0004", stock: 5);
        var created = await _client.PostAsJsonAsync(
            "/api/orders",
            new { currency = "CLP", lines = new[] { new { productId, quantity = 1 } } },
            TestContext.Current.CancellationToken);
        var orderId = (await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("id").GetGuid();

        await _client.PostAsync($"/api/orders/{orderId}/cancel", content: null, TestContext.Current.CancellationToken);
        var second = await _client.PostAsync($"/api/orders/{orderId}/cancel", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Order.NotPlaced", problem.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Cancel_ConPedidoInexistente_Devuelve404()
    {
        var response = await _client.PostAsync($"/api/orders/{Guid.NewGuid()}/cancel", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_DeUnProductoConPedidos_Devuelve409()
    {
        // Regla entre agregados, verificada de punta a punta.
        var productId = await CreateProductAsync("ORD-0005", stock: 5);
        await _client.PostAsJsonAsync(
            "/api/orders",
            new { currency = "CLP", lines = new[] { new { productId, quantity = 1 } } },
            TestContext.Current.CancellationToken);

        var response = await _client.DeleteAsync($"/api/products/{productId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Product.UsedInOrders", problem.GetProperty("errorCode").GetString());
    }
}
