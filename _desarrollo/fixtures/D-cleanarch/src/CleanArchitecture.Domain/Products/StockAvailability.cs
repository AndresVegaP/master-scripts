namespace CleanArchitecture.Domain.Products;

/// <summary>Falta de stock detectada para un producto concreto.</summary>
/// <param name="Sku">Producto afectado.</param>
/// <param name="Available">Unidades disponibles.</param>
/// <param name="Requested">Unidades solicitadas.</param>
public sealed record StockShortage(Sku Sku, int Available, int Requested);

/// <summary>Unidades que se quieren descontar de un producto.</summary>
/// <param name="Product">Producto del que se descuenta.</param>
/// <param name="Quantity">Unidades solicitadas.</param>
public sealed record StockRequest(Product Product, int Quantity);

/// <summary>
/// SERVICIO DE DOMINIO: comprueba de una sola vez si hay stock para todas las
/// unidades solicitadas.
/// </summary>
public static class StockAvailability
{
    // 📘 docs/05-agregados.md
    //
    // ¿QUÉ ES UN SERVICIO DE DOMINIO? Una regla de negocio que no pertenece a
    // NINGUNA entidad ni value object en particular, normalmente porque mira
    // VARIOS agregados a la vez. Aquí es el caso: "¿alcanza el stock para todo
    // el pedido?" no es una pregunta de UN producto, sino de todos los del
    // pedido juntos.
    //
    // Tres reglas para que un servicio de dominio no se convierta en el
    // "servicio anémico" que criticamos:
    //   1. Es PURO: no toca base de datos, red ni reloj. Recibe todo lo que
    //      necesita y devuelve una respuesta. Por eso se prueba sin montar nada.
    //   2. Habla el lenguaje del negocio (StockShortage, no List<string>).
    //   3. NO reemplaza la invariante: Product.RemoveStock sigue negándose a
    //      dejar el stock en negativo. Este servicio existe para poder avisar
    //      de TODOS los productos sin stock de una vez, en lugar de fallar en
    //      el primero. La última palabra siempre la tiene el agregado.

    /// <summary>Devuelve los productos cuyo stock no alcanza. Lista vacía significa que se puede continuar.</summary>
    public static IReadOnlyList<StockShortage> FindShortages(IEnumerable<StockRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        return
        [
            .. requests
                .Where(request => request.Quantity > request.Product.Stock)
                .Select(request => new StockShortage(
                    request.Product.Sku,
                    request.Product.Stock,
                    request.Quantity)),
        ];
    }
}
