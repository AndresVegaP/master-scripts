using System.Net.Http.Json;
using System.Text.Json;

namespace CleanArchitecture.IntegrationTests.Api;

/// <summary>Tests del documento OpenAPI que publica la API.</summary>
public class OpenApiDocumentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // 📘 docs/08-api-http-y-openapi.md
    //
    // ¿POR QUÉ PROBAR LA ESPECIFICACIÓN? Porque es un CONTRATO: hay gente que
    // genera clientes a partir de ella. Estos tests fallan si alguien agrega un
    // endpoint sin documentar sus errores, si se pierde un operationId (que es
    // el nombre del método en los clientes generados) o si se cambia de versión
    // de OpenAPI sin querer.
    //
    // Ojo con el vocabulario: esto NO es "snapshot testing" (comparar la salida
    // completa contra un archivo de referencia). Aquí verificamos REGLAS del
    // documento, que es menos frágil: agregar un endpoint nuevo no rompe los
    // tests, pero documentarlo mal, sí. Y no tiene nada que ver con el patrón
    // Snapshot del dominio, que es otra cosa (ver docs/06).

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<JsonElement> GetDocumentAsync() =>
        await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", TestContext.Current.CancellationToken);

    [Fact]
    public async Task ElDocumento_UsaOpenApi30()
    {
        // .NET 10 genera 3.1 por defecto; este repo fija 3.0 a propósito porque
        // buena parte del tooling todavía no lee 3.1.
        var document = await GetDocumentAsync();

        Assert.StartsWith("3.0", document.GetProperty("openapi").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ElDocumento_TieneLosMetadatosGenerales()
    {
        var document = await GetDocumentAsync();
        var info = document.GetProperty("info");

        Assert.False(string.IsNullOrWhiteSpace(info.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(info.GetProperty("description").GetString()));
        Assert.Equal("v1", info.GetProperty("version").GetString());
        Assert.True(info.TryGetProperty("contact", out _));
        Assert.Equal("MIT", info.GetProperty("license").GetProperty("name").GetString());
    }

    [Fact]
    public async Task CadaOperacion_TieneOperationIdResumenYEtiqueta()
    {
        var document = await GetDocumentAsync();

        foreach (var path in document.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                var name = $"{operation.Name.ToUpperInvariant()} {path.Name}";

                Assert.True(operation.Value.TryGetProperty("operationId", out var operationId), $"{name} no tiene operationId");
                Assert.False(string.IsNullOrWhiteSpace(operationId.GetString()), $"{name} tiene un operationId vacío");
                Assert.True(operation.Value.TryGetProperty("summary", out _), $"{name} no tiene summary");
                Assert.True(operation.Value.TryGetProperty("tags", out _), $"{name} no tiene tags");
            }
        }
    }

    [Fact]
    public async Task LasEtiquetas_TienenDescripcion()
    {
        var document = await GetDocumentAsync();

        var tags = document.GetProperty("tags").EnumerateArray().ToList();

        Assert.Equal(2, tags.Count);
        Assert.All(tags, tag => Assert.False(string.IsNullOrWhiteSpace(tag.GetProperty("description").GetString())));
    }

    [Fact]
    public async Task CrearProducto_DocumentaTodasSusRespuestas()
    {
        var document = await GetDocumentAsync();
        var responses = document.GetProperty("paths").GetProperty("/api/products").GetProperty("post").GetProperty("responses");

        // El contrato completo: éxito y TODOS los errores que el cliente puede
        // recibir. Sin esto, quien consume la API se entera de los errores en
        // producción.
        Assert.True(responses.TryGetProperty("201", out var created));
        Assert.True(responses.TryGetProperty("400", out _));
        Assert.True(responses.TryGetProperty("409", out var conflict));
        Assert.True(responses.TryGetProperty("422", out _));

        Assert.True(created.GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(conflict.GetProperty("content").TryGetProperty("application/problem+json", out _));
    }

    [Fact]
    public async Task LosEsquemas_IncluyenLasDescripcionesDeLosComentariosXml()
    {
        // Comprueba que la documentación del código viaja hasta la
        // especificación: el <param name="sku"> del contrato aparece aquí.
        var document = await GetDocumentAsync();
        var sku = document
            .GetProperty("components").GetProperty("schemas")
            .GetProperty("CreateProductRequest")
            .GetProperty("properties").GetProperty("sku");

        Assert.Contains("TEC-0001", sku.GetProperty("description").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LosImportes_SePublicanComoNumeros()
    {
        // Sin JsonNumberHandling.Strict, cada importe saldría como
        // "number o string", que es confuso para quien genera un cliente.
        var document = await GetDocumentAsync();
        var price = document
            .GetProperty("components").GetProperty("schemas")
            .GetProperty("ProductResponse")
            .GetProperty("properties").GetProperty("price");

        Assert.Equal("number", price.GetProperty("type").GetString());
    }

    [Fact]
    public async Task LosCamposOpcionales_SeDeclaranNullableComoEn30()
    {
        // En OpenAPI 3.0 lo opcional se marca con "nullable": true (en 3.1 sería
        // "type": ["string", "null"]). Es la diferencia más visible entre ambas.
        var document = await GetDocumentAsync();
        var updatedAt = document
            .GetProperty("components").GetProperty("schemas")
            .GetProperty("ProductResponse")
            .GetProperty("properties").GetProperty("updatedAt");

        Assert.True(updatedAt.GetProperty("nullable").GetBoolean());
    }
}
