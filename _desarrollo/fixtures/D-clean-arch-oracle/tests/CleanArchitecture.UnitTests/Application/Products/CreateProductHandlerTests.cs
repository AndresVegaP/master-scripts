using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Application.Products.CreateProduct;
using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Products;
using CleanArchitecture.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace CleanArchitecture.UnitTests.Application.Products;

/// <summary>Tests del caso de uso "crear producto".</summary>
public class CreateProductHandlerTests
{
    // AQUÍ SE COSECHA LO QUE SEMBRÓ LA ARQUITECTURA: probamos el caso de uso
    // completo (validación, regla de unicidad, creación y persistencia) SIN base
    // de datos, enchufando dobles por las interfaces. Milisegundos por test.
    //
    // Qué probar de un handler, como lista de control:
    //   ✓ el camino feliz y sus EFECTOS (se guardó, la respuesta es correcta)
    //   ✓ cada fallo esperado (el Result correcto)
    //   ✓ que los fallos NO dejan efectos a medias

    private readonly InMemoryProductRepository _repository = new();
    private readonly FakeTimeProvider _clock = new(Some.Now);
    private readonly CreateProductHandler _sut;

    public CreateProductHandlerTests() => _sut = new CreateProductHandler(_repository, _clock);

    private static CreateProductCommand ValidCommand(string sku = "TEC-0001") =>
        new(sku, "Teclado mecánico", 89990m, "CLP", 10);

    [Fact]
    public async Task ExecuteAsync_ConDatosValidos_DevuelveElProductoCreado()
    {
        var result = await _sut.ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal("TEC-0001", result.Value.Sku);
        Assert.Equal(89990m, result.Value.Price);
        Assert.Equal("CLP", result.Value.Currency);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
    }

    [Fact]
    public async Task ExecuteAsync_ConDatosValidos_PersisteElProducto()
    {
        var result = await _sut.ExecuteAsync(ValidCommand());

        // Verificamos el EFECTO a través de la interfaz pública del repositorio,
        // no de sus detalles internos.
        var saved = await _repository.GetByIdAsync(new ProductId(result.Value.Id));

        Assert.NotNull(saved);
        Assert.Equal("TEC-0001", saved.Sku.Value);
    }

    [Fact]
    public async Task ExecuteAsync_UsaElRelojInyectado()
    {
        // El dominio no lee la hora del sistema: la recibe. Por eso el test puede
        // fijarla y comprobar la fecha exacta, sin tolerancias ni sorpresas.
        var result = await _sut.ExecuteAsync(ValidCommand());

        Assert.Equal(Some.Now, result.Value.CreatedAt);
        Assert.Null(result.Value.UpdatedAt);
    }

    [Fact]
    public async Task ExecuteAsync_ConSkuDuplicado_DevuelveConflicto()
    {
        await _sut.ExecuteAsync(ValidCommand("TEC-0001"));

        var result = await _sut.ExecuteAsync(ValidCommand("TEC-0001"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Product.SkuAlreadyExists", result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_ConSkuDuplicado_NoPersisteNada()
    {
        await _sut.ExecuteAsync(ValidCommand("TEC-0001"));

        await _sut.ExecuteAsync(ValidCommand("TEC-0001"));

        Assert.Equal(1, _repository.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ConDatosInvalidos_DejaSalirLaDomainException()
    {
        // El handler NO captura las excepciones de dominio: eso es correcto.
        // La capa Api las traduce a 422 con su código de error.
        var command = ValidCommand() with { Sku = "no-es-un-sku" };

        var exception = await Assert.ThrowsAsync<DomainException>(() => _sut.ExecuteAsync(command));

        Assert.Equal("Sku.InvalidFormat", exception.Code);
    }
}
