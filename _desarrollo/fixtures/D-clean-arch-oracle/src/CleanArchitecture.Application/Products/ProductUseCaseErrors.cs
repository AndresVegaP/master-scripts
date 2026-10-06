using CleanArchitecture.Application.Abstractions;

namespace CleanArchitecture.Application.Products;

/// <summary>Fallos esperados de los casos de uso de productos.</summary>
public static class ProductUseCaseErrors
{
    // 📘 docs/adr/0003-estrategia-de-errores.md
    //
    // OJO, ESTO ES IMPORTANTE: existe otro catálogo llamado ProductErrors en la
    // capa Domain. No es un descuido, es la distinción central de la estrategia
    // de errores del repo:
    //
    //   Domain/Products/ProductErrors     → REGLAS DE NEGOCIO rotas. El agregado
    //                                       lo sabe con lo que tiene en memoria.
    //                                       Excepción → 422 o 409.
    //   Application/.../ProductUseCaseErrors → resultados que solo se conocen
    //                                       CONSULTANDO EL ALMACÉN (no existe,
    //                                       ya existe, cambió). Result → 404 o 409.
    //
    // Por eso este se llama "UseCase": para que al leerlo sepas de cuál hablas.

    /// <summary>El producto no existe.</summary>
    public static Error NotFound(Guid productId) =>
        Error.NotFound("Product.NotFound", $"No existe ningún producto con el Id {productId}.");

    /// <summary>Ya hay otro producto con ese SKU.</summary>
    public static Error SkuAlreadyExists(string sku) =>
        Error.Conflict("Product.SkuAlreadyExists", $"Ya existe un producto con el SKU {sku}.");

    /// <summary>Otra operación modificó el producto mientras se procesaba esta.</summary>
    public static Error ConcurrencyConflict(Guid productId) =>
        Error.Conflict(
            "Product.ConcurrencyConflict",
            $"El producto {productId} fue modificado o eliminado por otra operación. Vuelve a leerlo e inténtalo de nuevo.");

    /// <summary>No se puede eliminar porque aparece en pedidos.</summary>
    public static Error UsedInOrders(Guid productId) =>
        Error.Conflict(
            "Product.UsedInOrders",
            $"No se puede eliminar el producto {productId} porque aparece en pedidos ya registrados.");
}
