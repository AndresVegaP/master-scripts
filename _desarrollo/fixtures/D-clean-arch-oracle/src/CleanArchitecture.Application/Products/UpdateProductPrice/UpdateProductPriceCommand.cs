namespace CleanArchitecture.Application.Products.UpdateProductPrice;

/// <summary>Datos para cambiar el precio de un producto.</summary>
/// <param name="ProductId">Producto afectado.</param>
/// <param name="Amount">Nuevo importe.</param>
/// <param name="Currency">Moneda del nuevo importe (debe coincidir con la actual).</param>
public sealed record UpdateProductPriceCommand(Guid ProductId, decimal Amount, string Currency);

// DETALLE DE DISEÑO: este comando cambia EL PRECIO, no "el producto". Podríamos
// tener un UpdateProductCommand con todos los campos, pero los comandos
// pequeños y específicos:
//   - expresan una intención de negocio real (cambiar el precio es una decisión;
//     "actualizar producto" es jerga de base de datos);
//   - evitan el clásico bug del PUT gigante que pisa campos que nadie quería tocar;
//   - se corresponden uno a uno con los métodos del agregado (ChangePrice).
