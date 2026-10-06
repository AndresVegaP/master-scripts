using CleanArchitecture.Domain.Products;

namespace CleanArchitecture.UnitTests.Fakes;

/// <summary>Repositorio de productos en memoria para los tests de casos de uso.</summary>
internal sealed class InMemoryProductRepository : IProductRepository
{
    // 📘 docs/09-tests.md
    //
    // VOCABULARIO DE DOBLES DE PRUEBA (sale en entrevistas):
    //   FAKE : implementación funcional simplificada (esta clase).
    //   STUB : devuelve respuestas fijas ("cuando te pidan X, responde Y").
    //   MOCK : además REGISTRA las llamadas para verificarlas después.
    //
    // ⭐ EL DETALLE MÁS IMPORTANTE DE ESTE FAKE: guarda SNAPSHOTS, no los
    // objetos. Una base de datos real no te devuelve el mismo objeto que
    // guardaste, sino una copia reconstruida. Si el fake guardara referencias,
    // los tests pasarían aunque el caso de uso se olvidara de llamar a
    // UpdateAsync: el objeto ya estaría modificado en memoria.
    //
    // Guardar snapshots hace que el fake se comporte como la base de datos, y
    // además permite simular la CONCURRENCIA OPTIMISTA igual que el adaptador
    // real (comparando la versión).

    private readonly Dictionary<ProductId, ProductSnapshot> _rows = [];

    /// <summary>Cantidad de productos guardados. Solo para las comprobaciones de los tests.</summary>
    public int Count => _rows.Count;

    /// <summary>
    /// Cuando es true, el próximo UpdateAsync devuelve false, como si otra
    /// petición hubiera modificado el producto entre la lectura y el guardado.
    /// Sirve para probar el camino de conflicto de concurrencia sin carreras
    /// reales (que serían lentas e inestables en un test).
    /// </summary>
    public bool SimulateConcurrentChange { get; set; }

    /// <summary>Guarda un producto directamente, sin pasar por un caso de uso.</summary>
    public void Seed(Product product) => _rows[product.Id] = product.ToSnapshot();

    // Simula PCK_PRODUCTOS.SP_OBTENER_PRODUCTO sin tocar la base de datos.
    public Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_rows.TryGetValue(id, out var snapshot) ? Product.FromSnapshot(snapshot) : null);

    // El adaptador real hace "SELECT PCK_PRODUCTOS.FN_EXISTE_SKU(:Sku) FROM DUAL";
    // aquí basta con mirar el diccionario.
    public Task<bool> ExistsWithSkuAsync(Sku sku, CancellationToken cancellationToken = default) =>
        Task.FromResult(_rows.Values.Any(row => row.Sku == sku.Value));

    public Task<bool> AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        var snapshot = product.ToSnapshot();

        // Igual que el UNIQUE de la tabla real.
        if (_rows.Values.Any(row => row.Sku == snapshot.Sku))
        {
            return Task.FromResult(false);
        }

        _rows[product.Id] = snapshot;

        return Task.FromResult(true);
    }

    public Task<bool> UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        if (SimulateConcurrentChange)
        {
            return Task.FromResult(false);
        }

        var snapshot = product.ToSnapshot();

        // Concurrencia optimista: si la versión guardada ya no coincide, el
        // UPDATE real afectaría 0 filas.
        if (!_rows.TryGetValue(product.Id, out var current) || current.Version != snapshot.Version)
        {
            return Task.FromResult(false);
        }

        _rows[product.Id] = snapshot with { Version = snapshot.Version + 1 };

        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(ProductId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_rows.Remove(id));
}
