namespace CleanArchitecture.Application.Abstractions;

/// <summary>Una página de resultados y los datos para navegar entre páginas.</summary>
/// <typeparam name="TItem">Tipo de los elementos de la página.</typeparam>
/// <param name="Items">Elementos de esta página.</param>
/// <param name="Page">Número de página, empezando en 1.</param>
/// <param name="PageSize">Tamaño de página solicitado.</param>
/// <param name="TotalCount">Total de elementos disponibles.</param>
public sealed record PagedResult<TItem>(
    IReadOnlyList<TItem> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    // 📘 docs/08-api-http-y-openapi.md
    //
    // Un listado sin paginar es una bomba de tiempo: funciona con 10 filas y
    // tumba el servidor con 10 millones. Devolver SIEMPRE una página (aunque
    // sea grande) es una de esas decisiones que cuestan poco al principio y
    // mucho después.

    /// <summary>Cantidad total de páginas con el tamaño de página actual.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>Indica si hay una página siguiente.</summary>
    public bool HasNextPage => Page < TotalPages;
}
