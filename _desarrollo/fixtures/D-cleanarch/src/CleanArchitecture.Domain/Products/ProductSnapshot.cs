namespace CleanArchitecture.Domain.Products;

/// <summary>
/// Foto inmutable del estado de un <see cref="Product"/>: todo lo que hace
/// falta para guardarlo y para volver a construirlo, en tipos simples.
/// </summary>
/// <param name="Id">Identidad del producto.</param>
/// <param name="Sku">Código de negocio, ya normalizado.</param>
/// <param name="Name">Nombre del producto.</param>
/// <param name="PriceAmount">Importe del precio.</param>
/// <param name="PriceCurrency">Código ISO de la moneda del precio.</param>
/// <param name="Stock">Unidades disponibles.</param>
/// <param name="CreatedAt">Momento de creación.</param>
/// <param name="UpdatedAt">Momento de la última modificación, o null si nunca cambió.</param>
/// <param name="Version">Versión para la concurrencia optimista.</param>
public sealed record ProductSnapshot(
    Guid Id,
    string Sku,
    string Name,
    decimal PriceAmount,
    string PriceCurrency,
    int Stock,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    int Version)
{
    // 📘 docs/06-repositorios-dapper-y-snapshots.md
    //
    // EL PATRÓN SNAPSHOT, en una frase: el agregado entrega una copia plana de
    // su estado (ToSnapshot) y sabe reconstruirse a partir de ella
    // (FromSnapshot), sin abrir su encapsulación a nadie.
    //
    // ¿Qué problema resuelve? Para guardar un Product en una base de datos hay
    // que leer su interior: el SKU, el importe del precio, la moneda... Si el
    // repositorio lo hace propiedad a propiedad (product.Price.Amount,
    // product.Sku.Value...), la capa de persistencia termina conociendo cómo
    // está construido el agregado por dentro, y cualquier cambio interno rompe
    // el SQL, el fake de los tests y la firma de rehidratación.
    //
    // Con el snapshot hay UN contrato explícito entre el dominio y quien lo
    // guarda:
    //
    //     Product  --ToSnapshot-->  ProductSnapshot  --Dapper-->  fila SQL
    //     Product  <--FromSnapshot--  ProductSnapshot  <--Dapper--  fila SQL
    //
    // Ventajas:
    //   - El agregado sigue con todo privado: nadie toca su estado por fuera.
    //   - Infrastructure solo conoce tipos simples (Guid, string, decimal).
    //   - El contrato es visible y versionable: si cambia, se ve en un archivo.
    //   - Sirve igual para persistir, para serializar o para un caché.
    //   - Funciona con agregados que tienen hijos (mira OrderSnapshot).
    //
    // Cuidado con una trampa: dos records se comparan por valor, PERO una
    // propiedad de tipo lista se compara por REFERENCIA. Por eso OrderSnapshot
    // documenta cómo comparar sus líneas en los tests.
    //
    // Nota: "snapshot" aquí es el patrón de persistencia. No lo confundas con
    // el "snapshot testing", que es otra cosa (comparar una salida con un
    // archivo de referencia).
}
