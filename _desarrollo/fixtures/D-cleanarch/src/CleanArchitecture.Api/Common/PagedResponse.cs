using CleanArchitecture.Application.Abstractions;

namespace CleanArchitecture.Api.Common;

/// <summary>Página de resultados devuelta por la API.</summary>
/// <typeparam name="TItem">Tipo de los elementos.</typeparam>
/// <param name="Items">Elementos de esta página.</param>
/// <param name="Page">Número de página, empezando en 1.</param>
/// <param name="PageSize">Cantidad de elementos por página.</param>
/// <param name="TotalCount">Total de elementos disponibles.</param>
/// <param name="TotalPages">Cantidad total de páginas.</param>
/// <param name="HasNextPage">Indica si existe una página siguiente.</param>
public sealed record PagedResponse<TItem>(
    IReadOnlyList<TItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasNextPage)
{
    /// <summary>Convierte la página de la capa Application en la respuesta pública.</summary>
    public static PagedResponse<TResponse> From<TDto, TResponse>(
        PagedResult<TDto> page,
        Func<TDto, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(map);

        return new PagedResponse<TResponse>(
            [.. page.Items.Select(map)],
            page.Page,
            page.PageSize,
            page.TotalCount,
            page.TotalPages,
            page.HasNextPage);
    }
}
