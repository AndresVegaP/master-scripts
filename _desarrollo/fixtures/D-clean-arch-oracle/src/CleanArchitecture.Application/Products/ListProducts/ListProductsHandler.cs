using CleanArchitecture.Application.Abstractions;

namespace CleanArchitecture.Application.Products.ListProducts;

/// <summary>Caso de uso: listar el catálogo, paginado.</summary>
public sealed class ListProductsHandler(IProductQueries productQueries)
{
    /// <summary>Tamaño máximo de página aceptado, para que nadie pida el catálogo entero.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Ejecuta el caso de uso.</summary>
    public async Task<Result<PagedResult<ProductDto>>> ExecuteAsync(
        ListProductsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Normalizamos la entrada en lugar de fallar: pedir la página 0 o un
        // tamaño de 5000 no es un error del negocio, es un cliente distraído.
        // Math.Clamp deja el valor dentro del rango permitido.
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var products = await productQueries.ListAsync(page, pageSize, cancellationToken);

        // Un listado no tiene fallo esperado: una página vacía es un éxito con
        // cero elementos. Devolvemos Result igual, por consistencia con el
        // resto de casos de uso y para que la Api los trate a todos igual.
        return Result.Success(products);
    }
}
