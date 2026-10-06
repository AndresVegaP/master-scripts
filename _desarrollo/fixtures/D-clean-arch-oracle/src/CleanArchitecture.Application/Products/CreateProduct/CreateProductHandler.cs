using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Domain.Products;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.Application.Products.CreateProduct;

/// <summary>Caso de uso: crear un producto en el catálogo.</summary>
public sealed class CreateProductHandler(IProductRepository productRepository, TimeProvider timeProvider)
{
    // 📘 docs/07-casos-de-uso.md
    //
    // ¿QUÉ ES UN CASO DE USO? Una acción completa que el sistema ofrece,
    // contada en lenguaje de negocio. En Clean Architecture, cada caso de uso
    // es UNA clase con UN método público. Eso aplica dos principios a la vez:
    //   - SRP: la clase tiene una sola razón para cambiar.
    //   - Screaming Architecture: abrir la carpeta te dice lo que hace la
    //     aplicación (CreateProduct, PlaceOrder...), en vez de esconderlo en un
    //     ProductService de 800 líneas.
    //
    // EL HANDLER ORQUESTA, NO DECIDE: las reglas viven en el dominio; aquí solo
    // se coordinan los pasos. Si ves un handler lleno de "if" de negocio, esas
    // reglas están en la capa equivocada.
    //
    // Las dependencias entran por el CONSTRUCTOR PRIMARIO (C# 12): se declaran
    // una vez en la firma de la clase y se usan en los métodos. Fíjate en los
    // tipos: la INTERFAZ del dominio y TimeProvider (la abstracción del reloj
    // de .NET), nunca Dapper ni DateTime.UtcNow. Por eso los tests pueden
    // enchufar un repositorio en memoria y congelar la hora.

    /// <summary>Ejecuta el caso de uso.</summary>
    public async Task<Result<ProductDto>> ExecuteAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // PASO 1 — Primitivos a VALUE OBJECTS. Aquí se validan las reglas de
        // negocio del dato: formato del SKU, largo del nombre, moneda admitida,
        // decimales del importe. Si algo no cumple, el VO lanza DomainException
        // y la Api responde 422. Después de estas líneas, todo es válido.
        var sku = Sku.Create(command.Sku);
        var name = ProductName.Create(command.Name);
        var price = Money.Create(command.Price, command.Currency);

        // PASO 2 — Regla que NECESITA la base de datos: el SKU es único. Ni el
        // value object ni el agregado pueden saberlo (no consultan nada), así
        // que es responsabilidad del caso de uso. Es un fallo esperado, no una
        // regla rota: por eso viaja como Result y no como excepción.
        if (await productRepository.ExistsWithSkuAsync(sku, cancellationToken))
        {
            return Result.Failure<ProductDto>(ProductUseCaseErrors.SkuAlreadyExists(sku.Value));
        }

        // PASO 3 — Crear el agregado con su fábrica (aplica sus invariantes).
        var product = Product.Create(sku, name, price, command.InitialStock, timeProvider.GetUtcNow());

        // PASO 4 — Guardar. AddAsync devuelve false si otra petición se adelantó
        // con el mismo SKU entre el PASO 2 y este momento (condición de carrera
        // real, protegida por el UNIQUE de la tabla). Respondemos lo mismo que
        // en el camino normal: 409, nunca un 500.
        if (!await productRepository.AddAsync(product, cancellationToken))
        {
            return Result.Failure<ProductDto>(ProductUseCaseErrors.SkuAlreadyExists(sku.Value));
        }

        // PASO 5 — Devolver la vista de salida. La conversión implícita de
        // Result envuelve esto en un resultado exitoso.
        return ProductDto.FromProduct(product);
    }
}
