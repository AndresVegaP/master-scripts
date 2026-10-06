using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.Domain.Products;

/// <summary>Catálogo de errores del agregado Product y de sus value objects.</summary>
public static class ProductErrors
{
    // 📘 docs/adr/0003-estrategia-de-errores.md
    //
    // Tener los errores en un catálogo, y no como textos sueltos donde se
    // lanzan, sirve para tres cosas:
    //   1. Da LENGUAJE UBICUO también a los fallos: "stock insuficiente" es un
    //      término que el negocio reconoce, y aquí tiene un nombre en el código.
    //   2. Evita duplicar mensajes y códigos por todo el dominio.
    //   3. Permite documentarlos en la API para que los clientes programen
    //      contra el código (estable) y no contra el mensaje (que puede cambiar).

    public static DomainError SkuRequired =>
        new("Sku.Required", "El SKU es obligatorio.", DomainErrorKind.Invariant);

    public static DomainError SkuInvalidFormat(string value) =>
        new(
            "Sku.InvalidFormat",
            $"El SKU {value} no tiene un formato válido. Formato esperado: 3 letras, guion y 4 dígitos (por ejemplo, TEC-0001).",
            DomainErrorKind.Invariant);

    public static DomainError NameRequired =>
        new("ProductName.Required", "El nombre del producto es obligatorio.", DomainErrorKind.Invariant);

    public static DomainError NameInvalidLength(int minLength, int maxLength, int received) =>
        new(
            "ProductName.InvalidLength",
            $"El nombre del producto debe tener entre {minLength} y {maxLength} caracteres (recibido: {received}).",
            DomainErrorKind.Invariant);

    public static DomainError NegativeStock(int received) =>
        new(
            "Product.NegativeStock",
            $"El stock no puede ser negativo (recibido: {received}).",
            DomainErrorKind.Invariant);

    public static DomainError NonPositiveQuantity(int received) =>
        new(
            "Product.NonPositiveQuantity",
            $"La cantidad debe ser mayor que cero (recibido: {received}).",
            DomainErrorKind.Invariant);

    public static DomainError InsufficientStock(Sku sku, int available, int requested) =>
        new(
            "Product.InsufficientStock",
            $"El producto {sku} tiene {available} unidades disponibles y se solicitaron {requested}.",
            DomainErrorKind.Conflict);

    public static DomainError CurrencyChangeNotAllowed(Currency current, Currency attempted) =>
        new(
            "Product.CurrencyChangeNotAllowed",
            $"El precio no puede cambiar de moneda ({current} a {attempted}): eso es otra operación de negocio.",
            DomainErrorKind.Conflict);
}
