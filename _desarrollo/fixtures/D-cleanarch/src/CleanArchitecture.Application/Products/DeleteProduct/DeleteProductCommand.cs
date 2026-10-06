namespace CleanArchitecture.Application.Products.DeleteProduct;

/// <summary>Datos para eliminar un producto del catálogo.</summary>
/// <param name="ProductId">Producto a eliminar.</param>
public sealed record DeleteProductCommand(Guid ProductId);
