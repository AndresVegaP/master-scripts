using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace CleanArchitecture.IntegrationTests.Api;

/// <summary>Tests de extremo a extremo del recurso productos.</summary>
public class ProductsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Estos tests verifican el CONTRATO PÚBLICO: rutas, códigos de estado,
    // cabeceras y forma del JSON. Es la red de seguridad contra romper a los
    // clientes sin darse cuenta. Puedes refactorizar todo por dentro y, si el
    // contrato no cambia, siguen en verde.
    //
    // IClassFixture: xUnit crea UNA aplicación (y una base) para toda la clase.
    // Arrancarla es caro; compartirla es el equilibrio pragmático. A cambio,
    // cada test usa SUS PROPIOS datos (fíjate en los SKU distintos).

    private readonly HttpClient _client = factory.CreateClient();

    private static object NewProduct(string sku, decimal price = 89990m, string currency = "CLP", int stock = 10) =>
        new { sku, name = "Teclado mecánico", price, currency, initialStock = stock };

    private async Task<JsonElement> CreateProductAsync(string sku, decimal price = 89990m, int stock = 10)
    {
        var response = await _client.PostAsJsonAsync("/api/products", NewProduct(sku, price, stock: stock), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Post_ConDatosValidos_Devuelve201ConLocationYElProducto()
    {
        var response = await _client.PostAsJsonAsync("/api/products", NewProduct("API-0001"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("API-0001", body.GetProperty("sku").GetString());
        Assert.Equal(89990m, body.GetProperty("price").GetDecimal());
        Assert.Equal("CLP", body.GetProperty("currency").GetString());

        // La cabecera Location apunta al recurso recién creado, y funciona.
        var fromLocation = await _client.GetAsync(response.Headers.Location, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, fromLocation.StatusCode);
    }

    [Fact]
    public async Task Post_ConSkuDuplicado_Devuelve409ConSuCodigoDeError()
    {
        await CreateProductAsync("API-0002");

        var response = await _client.PostAsJsonAsync("/api/products", NewProduct("API-0002"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Product.SkuAlreadyExists", problem.GetProperty("errorCode").GetString());
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Post_SinCamposObligatorios_Devuelve400ConElDetallePorCampo()
    {
        // Validación de FORMA: la hace la capa Api antes de llegar al dominio.
        var response = await _client.PostAsJsonAsync("/api/products", new { sku = "API-0003" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Request.ValidationFailed", problem.GetProperty("errorCode").GetString());

        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("Price", out _));
        Assert.True(errors.TryGetProperty("Currency", out _));
    }

    [Fact]
    public async Task Post_ConSkuMalFormado_Devuelve422PorqueEsUnaReglaDeNegocio()
    {
        // Validación de NEGOCIO: la hace el dominio. El JSON está bien formado y
        // completo (por eso no es 400), pero rompe una regla: 422.
        var response = await _client.PostAsJsonAsync("/api/products", NewProduct("no-es-un-sku"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Sku.InvalidFormat", problem.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Post_ConDecimalesQueLaMonedaNoAdmite_Devuelve422()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/products",
            new { sku = "API-0004", name = "Producto", price = 1990.55m, currency = "CLP", initialStock = 1 },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Money.TooManyDecimals", problem.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Post_ConJsonMalformado_Devuelve400()
    {
        // Un cuerpo que ni siquiera es JSON: ASP.NET Core lanza
        // BadHttpRequestException ANTES de llegar a nuestro endpoint, y el
        // manejador global la traduce a 400 (culpa del cliente), no a 500.
        var content = new StringContent("{ esto no es json", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/products", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Request.Malformed", problem.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Get_ConIdInexistente_Devuelve404ConProblemDetails()
    {
        var response = await _client.GetAsync($"/api/products/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Product.NotFound", problem.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Get_ConIdQueNoEsGuid_Devuelve404DelEnrutador()
    {
        // La restricción {id:guid} de la ruta filtra la petición antes de que
        // llegue a nuestro código, y UseStatusCodePages le da cuerpo.
        var response = await _client.GetAsync("/api/products/no-es-un-guid", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Put_CambiandoDeMoneda_Devuelve409()
    {
        var product = await CreateProductAsync("API-0005");

        var response = await _client.PutAsJsonAsync(
            $"/api/products/{product.GetProperty("id").GetGuid()}/price",
            new { amount = 100m, currency = "USD" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Product.CurrencyChangeNotAllowed", problem.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task GetList_DevuelveUnaPaginaConSusMetadatos()
    {
        await CreateProductAsync("API-0006");

        var response = await _client.GetAsync("/api/products?page=1&pageSize=1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.True(page.GetProperty("totalCount").GetInt32() >= 1);
        Assert.Equal(1, page.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task GetList_SinParametros_Funciona()
    {
        // Los parámetros de paginación son OPCIONALES: la ruta más simple tiene
        // que funcionar tal cual.
        var response = await _client.GetAsync("/api/products", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetList_ConPaginacionInvalida_Devuelve400()
    {
        var response = await _client.GetAsync("/api/products?page=0&pageSize=9999", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CicloDeVidaCompleto_CrearLeerActualizarBorrar()
    {
        // Un test de "viaje completo" que documenta cómo se usa la API de
        // principio a fin. Complementa (no sustituye) a los tests pequeños.
        var created = await CreateProductAsync("API-0007");
        var id = created.GetProperty("id").GetGuid();

        var fetched = await _client.GetFromJsonAsync<JsonElement>($"/api/products/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(id, fetched.GetProperty("id").GetGuid());
        Assert.True(fetched.GetProperty("updatedAt").ValueKind is JsonValueKind.Null);

        var updateResponse = await _client.PutAsJsonAsync(
            $"/api/products/{id}/price",
            new { amount = 79990m, currency = "CLP" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(79990m, updated.GetProperty("price").GetDecimal());
        Assert.False(updated.GetProperty("updatedAt").ValueKind is JsonValueKind.Null);

        var deleteResponse = await _client.DeleteAsync($"/api/products/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var goneResponse = await _client.GetAsync($"/api/products/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, goneResponse.StatusCode);
    }
}
