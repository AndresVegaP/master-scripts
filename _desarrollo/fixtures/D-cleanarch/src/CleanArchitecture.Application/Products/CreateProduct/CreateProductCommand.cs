namespace CleanArchitecture.Application.Products.CreateProduct;

/// <summary>Datos para crear un producto.</summary>
/// <param name="Sku">Código de negocio (formato ABC-1234).</param>
/// <param name="Name">Nombre visible.</param>
/// <param name="Price">Importe del precio inicial.</param>
/// <param name="Currency">Código ISO de la moneda.</param>
/// <param name="InitialStock">Unidades iniciales.</param>
public sealed record CreateProductCommand(
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    int InitialStock);

// 📘 docs/07-casos-de-uso.md
//
// ¿Qué es un COMANDO? Un objeto que representa la INTENCIÓN de cambiar el
// estado del sistema, con todos los datos necesarios. Es la C de CQRS:
//
//   COMMAND → cambia estado (crear, cambiar precio, cancelar). Este archivo.
//   QUERY   → solo lee, no cambia nada (ver ListProductsQuery).
//
// Fíjate en que los campos son PRIMITIVOS, no value objects: el comando
// representa lo que llegó de fuera, tal cual. Convertirlo a value objects (y
// validarlo) es trabajo del handler. Frontera clara: fuera primitivos, dentro
// tipos ricos.
