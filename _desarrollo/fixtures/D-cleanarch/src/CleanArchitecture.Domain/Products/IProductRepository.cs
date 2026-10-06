namespace CleanArchitecture.Domain.Products;

/// <summary>Puerto de persistencia del agregado Product.</summary>
public interface IProductRepository
{
    // 📘 docs/02-clean-architecture.md — ESTE ARCHIVO ES EL CORAZÓN DEL REPO.
    //
    // ¿Qué es un PUERTO? Una interfaz que declara QUÉ necesita el dominio
    // (guárdame este producto, búscamelo por Id) sin decir CÓMO se hace. El
    // CÓMO (Dapper, SQLite, SQL Server, memoria) vive en Infrastructure, que
    // implementa esta interfaz: eso es un ADAPTADOR.
    //
    // Esto es la INVERSIÓN DE DEPENDENCIAS (la D de SOLID):
    //
    //   SIN inversión:  Application ──depende──▶ Infrastructure (atado a la BD)
    //   CON inversión:  Application ──depende──▶ IProductRepository (interfaz)
    //                   Infrastructure ──implementa──▶ IProductRepository
    //
    // Las flechas apuntan HACIA el dominio. Consecuencias prácticas:
    //   - Puedes cambiar SQLite por PostgreSQL sin tocar dominio ni casos de uso.
    //   - Puedes probar los casos de uso con un repositorio en memoria.
    //
    // ¿Por qué está en Domain y no en Application? Porque el repositorio habla
    // el lenguaje del dominio (recibe y devuelve agregados) y el patrón
    // Repository es un concepto de DDD. Otras plantillas lo ponen en
    // Application; lo importante no es la carpeta, sino que la interfaz viva en
    // el centro y la implementación fuera.
    //
    // Fíjate en lo que este puerto NO tiene: métodos de listado o de búsqueda
    // para pantallas. Un repositorio de agregado sirve para CAMBIAR el estado;
    // las consultas de lectura van por otro camino (IProductQueries en
    // Application), y eso es CQRS. Ver docs/07-casos-de-uso.md.

    /// <summary>Busca un producto por su identidad. Null si no existe.</summary>
    Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indica si ya hay un producto con ese SKU. Existe como método propio
    /// porque un EXISTS en SQL es más barato que traer y reconstruir el agregado.
    /// </summary>
    Task<bool> ExistsWithSkuAsync(Sku sku, CancellationToken cancellationToken = default);

    /// <summary>Guarda un producto nuevo.</summary>
    /// <returns>
    /// false si otro producto ya tiene ese SKU. Devolver un bool (y no lanzar)
    /// permite responder 409 igual que en el camino normal, incluso cuando dos
    /// peticiones simultáneas compiten por el mismo SKU.
    /// </returns>
    Task<bool> AddAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>Guarda los cambios de un producto existente.</summary>
    /// <returns>
    /// false si el producto ya no existe o si alguien lo modificó antes (la
    /// versión del agregado ya no coincide con la de la base de datos).
    /// </returns>
    Task<bool> UpdateAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>Elimina un producto.</summary>
    /// <returns>false si el producto no existía.</returns>
    Task<bool> DeleteAsync(ProductId id, CancellationToken cancellationToken = default);
}
