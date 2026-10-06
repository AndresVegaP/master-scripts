using System.ComponentModel.DataAnnotations;
using CleanArchitecture.Application.Products;
using CleanArchitecture.Application.Products.ListProducts;

namespace CleanArchitecture.Api.Products;

/// <summary>Cuerpo de la petición para crear un producto.</summary>
/// <param name="Sku">Código único del producto. Formato: 3 letras, guion y 4 dígitos (por ejemplo, TEC-0001).</param>
/// <param name="Name">Nombre visible del producto (entre 3 y 100 caracteres).</param>
/// <param name="Price">Precio inicial. No negativo y con los decimales que admita la moneda.</param>
/// <param name="Currency">Moneda ISO 4217. Admitidas: USD, EUR, MXN, COP, PEN, CLP.</param>
/// <param name="InitialStock">Unidades iniciales en almacén.</param>
public sealed record CreateProductRequest(
    [Required] string? Sku,
    [Required] string? Name,
    [Required] decimal? Price,
    [Required] string? Currency,
    [Range(0, int.MaxValue)] int InitialStock);

// 📘 docs/08-api-http-y-openapi.md
//
// ¿POR QUÉ ESTE CONTRATO EXISTE SI EL COMANDO TIENE LOS MISMOS CAMPOS?
// Porque son cosas distintas que hoy coinciden:
//   - Este record es el CONTRATO PÚBLICO con los clientes HTTP. Cambiarlo rompe
//     a terceros: evoluciona despacio y con versionado.
//   - El comando es un detalle INTERNO de Application: se puede refactorizar
//     cuando haga falta.
// El día que el comando necesite un dato interno (el usuario que crea el
// producto, por ejemplo), el contrato público no cambia solo por eso.
//
// ¿POR QUÉ LOS TIPOS SON NULLABLE (string?, decimal?) SI LUEGO SON OBLIGATORIOS?
// Para poder distinguir "no lo mandaste" de "mandaste un valor". Si Price fuera
// decimal a secas, un JSON sin ese campo llegaría como 0 y crearíamos un
// producto gratis sin darnos cuenta. Con decimal? + [Required], la validación
// responde 400 diciendo exactamente qué campo falta.

/// <summary>Cuerpo de la petición para cambiar el precio de un producto.</summary>
/// <param name="Amount">Nuevo importe del precio.</param>
/// <param name="Currency">Moneda del nuevo precio. Debe coincidir con la actual del producto.</param>
public sealed record UpdateProductPriceRequest(
    [Required] decimal? Amount,
    [Required] string? Currency);

/// <summary>Representación pública de un producto.</summary>
/// <param name="Id">Identificador del producto.</param>
/// <param name="Sku">Código único del producto.</param>
/// <param name="Name">Nombre visible.</param>
/// <param name="Price">Precio actual.</param>
/// <param name="Currency">Moneda del precio.</param>
/// <param name="Stock">Unidades disponibles.</param>
/// <param name="CreatedAt">Fecha y hora de creación.</param>
/// <param name="UpdatedAt">Fecha y hora de la última modificación, o null si nunca se modificó.</param>
public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    int Stock,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>Convierte la salida del caso de uso en la respuesta pública.</summary>
    public static ProductResponse FromDto(ProductDto product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new ProductResponse(
            product.Id,
            product.Sku,
            product.Name,
            product.Price,
            product.Currency,
            product.Stock,
            product.CreatedAt,
            product.UpdatedAt);
    }
}
