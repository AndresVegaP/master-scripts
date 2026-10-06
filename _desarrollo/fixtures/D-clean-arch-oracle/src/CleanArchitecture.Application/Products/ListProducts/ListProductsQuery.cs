namespace CleanArchitecture.Application.Products.ListProducts;

/// <summary>Pide una página del catálogo.</summary>
/// <param name="Page">Número de página, empezando en 1.</param>
/// <param name="PageSize">Cantidad de elementos por página.</param>
public sealed record ListProductsQuery(int Page = 1, int PageSize = 20);
