using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Application.Products.UpdateProductPrice;
using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Products;
using CleanArchitecture.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace CleanArchitecture.UnitTests.Application.Products;

/// <summary>Tests del caso de uso "cambiar el precio".</summary>
public class UpdateProductPriceHandlerTests
{
    private readonly InMemoryProductRepository _repository = new();
    private readonly FakeTimeProvider _clock = new(Some.Now);
    private readonly UpdateProductPriceHandler _sut;

    public UpdateProductPriceHandlerTests() => _sut = new UpdateProductPriceHandler(_repository, _clock);

    [Fact]
    public async Task ExecuteAsync_ConProductoExistente_GuardaElPrecioNuevo()
    {
        var product = Some.Product(price: 89990m);
        _repository.Seed(product);

        var result = await _sut.ExecuteAsync(new UpdateProductPriceCommand(product.Id.Value, 79990m, "CLP"));

        Assert.True(result.IsSuccess);
        Assert.Equal(79990m, result.Value.Price);

        // ⭐ LA COMPROBACIÓN QUE DE VERDAD IMPORTA: volver a LEER del repositorio.
        // Como el doble guarda snapshots (copias), si el handler se olvidara de
        // llamar a UpdateAsync este test fallaría. Con un doble que guardara
        // referencias, pasaría igualmente y el bug llegaría a producción.
        var saved = await _repository.GetByIdAsync(product.Id);
        Assert.Equal(79990m, saved!.Price.Amount);
    }

    [Fact]
    public async Task ExecuteAsync_ConProductoInexistente_DevuelveNotFound()
    {
        var result = await _sut.ExecuteAsync(new UpdateProductPriceCommand(Guid.NewGuid(), 100m, "CLP"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Product.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_SiOtraPeticionLoModificoAntes_DevuelveConflicto()
    {
        var product = Some.Product();
        _repository.Seed(product);
        _repository.SimulateConcurrentChange = true;

        var result = await _sut.ExecuteAsync(new UpdateProductPriceCommand(product.Id.Value, 79990m, "CLP"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Product.ConcurrencyConflict", result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_CambiandoDeMoneda_DejaSalirLaDomainException()
    {
        // La regla vive en el AGREGADO; aquí solo comprobamos que el handler no
        // se la traga.
        var product = Some.Product(); // precio en CLP
        _repository.Seed(product);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _sut.ExecuteAsync(new UpdateProductPriceCommand(product.Id.Value, 99.99m, "USD")));

        Assert.Equal("Product.CurrencyChangeNotAllowed", exception.Code);
    }

    [Fact]
    public async Task ExecuteAsync_ConPrecioInvalido_DejaSalirLaDomainException()
    {
        var product = Some.Product();
        _repository.Seed(product);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _sut.ExecuteAsync(new UpdateProductPriceCommand(product.Id.Value, -5m, "CLP")));

        Assert.Equal("Money.NegativeAmount", exception.Code);
    }
}
