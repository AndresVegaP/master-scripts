using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Domain.Products;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.Application.Products.UpdateProductPrice;

/// <summary>Caso de uso: cambiar el precio de un producto.</summary>
public sealed class UpdateProductPriceHandler(IProductRepository productRepository, TimeProvider timeProvider)
{
    // Este handler muestra el patrón de MODIFICACIÓN, que siempre tiene la
    // misma forma:
    //
    //     cargar el agregado → invocar SU método de negocio → guardar
    //
    // Fíjate en el paso del medio: el handler NO comprueba ni calcula nada del
    // cambio de precio. La regla "no se puede cambiar de moneda" vive en
    // Product.ChangePrice. El handler orquesta; el dominio decide.

    /// <summary>Ejecuta el caso de uso.</summary>
    public async Task<Result<ProductDto>> ExecuteAsync(
        UpdateProductPriceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Validar la forma del dato creando el value object (puede lanzar
        //    DomainException → 422).
        var newPrice = Money.Create(command.Amount, command.Currency);

        // 2. Cargar el agregado.
        var product = await productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);

        if (product is null)
        {
            return Result.Failure<ProductDto>(ProductUseCaseErrors.NotFound(command.ProductId));
        }

        // 3. El AGREGADO aplica el cambio y sus reglas.
        product.ChangePrice(newPrice, timeProvider.GetUtcNow());

        // 4. Guardar. Devuelve false si el producto se borró mientras tanto o si
        //    otra petición lo modificó primero (concurrencia optimista: la
        //    versión que tenemos ya no es la de la base de datos). Antes esto
        //    respondía 200 como si nada, perdiendo el cambio en silencio.
        if (!await productRepository.UpdateAsync(product, cancellationToken))
        {
            return Result.Failure<ProductDto>(ProductUseCaseErrors.ConcurrencyConflict(command.ProductId));
        }

        return ProductDto.FromProduct(product);
    }
}
