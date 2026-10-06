using System.Data;
using System.Globalization;
using CleanArchitecture.Domain.Products;
using Dapper;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace CleanArchitecture.Infrastructure.Persistence.Products;

/// <summary>Adaptador con Dapper del puerto <see cref="IProductRepository"/>.</summary>
internal sealed class ProductRepository(DbSession session, ILogger<ProductRepository> logger) : IProductRepository
{
    // 📘 docs/06-repositorios-dapper-y-snapshots.md
    //
    // ESTE ARCHIVO ES LA OTRA MITAD DE LA INVERSIÓN DE DEPENDENCIAS: el dominio
    // declaró la interfaz y aquí, en la capa más externa, vive el código que de
    // verdad habla SQL. Ninguna capa interior sabe que esta clase existe.
    //
    // ¿QUÉ ES DAPPER? Un "micro-ORM": tú escribes el SQL y Dapper automatiza lo
    // tedioso de ADO.NET (recorrer filas, mapear columnas a propiedades).
    // Comparado con Entity Framework: control total del SQL y curva de
    // aprendizaje casi nula, a cambio de escribirlo todo a mano.
    //
    // LAS REGLAS DE ORO AL USAR DAPPER, y las verás aplicadas en cada método:
    //  1. SQL SIEMPRE PARAMETRIZADO (:Nombre en Oracle). Concatenar valores en
    //     el SQL es la puerta a la inyección SQL.
    //  2. Pasar SIEMPRE la transacción y el CancellationToken. En Dapper eso se
    //     hace con CommandDefinition: los métodos cortos (ExecuteAsync(sql, p))
    //     NO aceptan token y lo ignoran en silencio.
    //  3. Que el repositorio hable con SNAPSHOTS, no con el interior del
    //     agregado: aquí no verás product.Price.Amount por ningún lado.
    //
    // MIGRACIÓN DESDE ORACLE: este repositorio reemplaza poco a poco al paquete
    // de productos. Lo que ya es SQL lleva encima el comentario "Migrado de ...";
    // lo que todavía se delega a la base de datos se invoca tal cual.

    // Columnas en el mismo orden que ProductRow.
    private const string SelectColumns =
        "Id, Sku, Name, PriceAmount, PriceCurrency, Stock, CreatedAt, UpdatedAt, Version";

    // Migrado de PCK_PRODUCTOS.SP_OBTENER_PRODUCTO
    // (el procedimiento devolvía la fila en parámetros OUT; ahora es un SELECT).
    public async Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);

        // QuerySingleOrDefaultAsync espera 0 o 1 filas: 0 devuelve null y 2 o
        // más lanza excepción (sería un bug, porque Id es clave primaria).
        // Elegir el método correcto documenta lo que esperas.
        var row = await connection.QuerySingleOrDefaultAsync<ProductRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM Products WHERE Id = :Id",
            new { Id = id.Value.ToString() },
            session.Transaction,
            cancellationToken: cancellationToken));

        // Fila -> snapshot -> agregado. El repositorio no construye value
        // objects: le entrega el snapshot al dominio y el dominio se reconstruye.
        return row is null ? null : Product.FromSnapshot(row.ToSnapshot());
    }

    public async Task<bool> ExistsWithSkuAsync(Sku sku, CancellationToken cancellationToken = default)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);

        try
        {
            // La función del paquete devuelve 1 si el SKU ya existe y 0 si no.
            // Se sigue usando porque además mira la tabla de SKU reservados.
            var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT CATALOGO.PCK_PRODUCTOS.FN_EXISTE_SKU(:Sku) FROM DUAL",
                new { Sku = sku.Value },
                session.Transaction,
                cancellationToken: cancellationToken));

            return exists == 1;
        }
        catch (OracleException exception)
        {
            logger.LogError(exception, "Falló PCK_PRODUCTOS.FN_EXISTE_SKU para el SKU {Sku}", sku.Value);
            throw;
        }
    }

    public async Task<bool> AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        var connection = await session.GetConnectionAsync(cancellationToken);

        try
        {
            // El nombre se guarda normalizado (mayúsculas, sin espacios dobles)
            // con la misma función que usan los procesos nocturnos de la base.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO Products (Id, Sku, Name, PriceAmount, PriceCurrency, Stock, CreatedAt, UpdatedAt, Version)
                VALUES (:Id, :Sku, pck_util.fn_normalizar_nombre(:Name), :PriceAmount, :PriceCurrency, :Stock, :CreatedAt, :UpdatedAt, :Version)
                """,
                ProductRow.ToParameters(product.ToSnapshot()),
                session.Transaction,
                cancellationToken: cancellationToken));

            return true;
        }
        catch (OracleException exception) when (exception.Number == 1)
        {
            // ORA-00001 (restricción única violada): otra petición creó el mismo
            // SKU entre la comprobación del caso de uso y este INSERT. La
            // restricción UNIQUE de la tabla lo impidió; aquí lo traducimos a
            // "no se pudo" para que la respuesta sea 409 y no un 500.
            return false;
        }
    }

    public async Task<bool> UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        var connection = await session.GetConnectionAsync(cancellationToken);

        // CONCURRENCIA OPTIMISTA: solo actualizamos si la versión en la base
        // sigue siendo la que leímos. Si otra petición la cambió, esta sentencia
        // afecta 0 filas y devolvemos false, en lugar de pisar su cambio sin
        // que nadie se entere (el clásico "lost update").
        // UpdatedAt lo pone PCK_UTIL.FN_FECHA_SERVIDOR: así todos los nodos de
        // la API usan el mismo reloj, el del servidor de base de datos.
        var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE Products
            SET Name = :Name,
                PriceAmount = :PriceAmount,
                PriceCurrency = :PriceCurrency,
                Stock = :Stock,
                UpdatedAt = PCK_UTIL.FN_FECHA_SERVIDOR(),
                Version = Version + 1
            WHERE Id = :Id AND Version = :Version
            """,
            ProductRow.ToParameters(product.ToSnapshot()),
            session.Transaction,
            cancellationToken: cancellationToken));

        return affectedRows > 0;
    }

    public async Task<bool> DeleteAsync(ProductId id, CancellationToken cancellationToken = default)
    {
        // La baja sigue en la base de datos: además de borrar la fila, limpia el
        // histórico de precios y deja constancia en la bitácora. Esa lógica
        // todavía no se ha migrado.
        var parameters = new DynamicParameters();
        parameters.Add("p_id", id.Value.ToString());
        parameters.Add("p_eliminados", dbType: DbType.Int32, direction: ParameterDirection.Output);

        await session.ExecuteSpAsync(ProcedimientosOracle.EliminarProducto, parameters, cancellationToken);

        // p_eliminados trae las filas borradas: nos dice gratis si existía.
        return parameters.Get<int>("p_eliminados") > 0;
    }

    // Pendiente de exponer en la API (ticket MIG-142): todavía nadie lo llama.
    public async Task<IReadOnlyList<string>> ListarDescontinuadosAsync(CancellationToken cancellationToken = default)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);

        var parameters = new OracleDynamicParameters();
        parameters.AddRefCursor("p_cursor");

        var skus = await connection.QueryAsync<string>(new CommandDefinition(
            "BEGIN PCK_PRODUCTOS.SP_LISTAR_DESCONTINUADOS(:p_cursor); END;",
            parameters,
            session.Transaction,
            cancellationToken: cancellationToken));

        return [.. skus];
    }

    /// <summary>
    /// La forma EXACTA de una fila de la tabla Products, con los tipos que
    /// devuelve el proveedor de Oracle (Guid y fechas viajan como texto).
    /// </summary>
    private sealed class ProductRow
    {
        public string Id { get; init; } = string.Empty;

        public string Sku { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string PriceAmount { get; init; } = string.Empty;

        public string PriceCurrency { get; init; } = string.Empty;

        public long Stock { get; init; }

        public string CreatedAt { get; init; } = string.Empty;

        public string? UpdatedAt { get; init; }

        public long Version { get; init; }

        /// <summary>Fila a snapshot: aquí y solo aquí se traducen los tipos de la base.</summary>
        public ProductSnapshot ToSnapshot() =>
            new(
                Guid.Parse(Id),
                Sku,
                Name,
                // InvariantCulture SIEMPRE al convertir datos: con la cultura del
                // sistema, "10.50" se leería como 1050 en países que usan la coma
                // como separador decimal.
                decimal.Parse(PriceAmount, CultureInfo.InvariantCulture),
                PriceCurrency,
                (int)Stock, // NUMBER llega como 64 bits
                DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture),
                UpdatedAt is null ? null : DateTimeOffset.Parse(UpdatedAt, CultureInfo.InvariantCulture),
                (int)Version);

        /// <summary>Snapshot a parámetros de SQL.</summary>
        public static object ToParameters(ProductSnapshot snapshot) => new
        {
            Id = snapshot.Id.ToString(),
            snapshot.Sku,
            snapshot.Name,
            snapshot.PriceAmount,
            snapshot.PriceCurrency,
            snapshot.Stock,
            snapshot.CreatedAt,
            snapshot.UpdatedAt,
            snapshot.Version,
        };
    }
}
