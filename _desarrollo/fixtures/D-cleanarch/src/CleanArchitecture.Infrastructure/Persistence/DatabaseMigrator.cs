using Dapper;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Infrastructure.Persistence;

/// <summary>Lleva la base de datos al último esquema aplicando migraciones en orden.</summary>
internal sealed class DatabaseMigrator(IDbConnectionFactory connectionFactory, ILogger<DatabaseMigrator> logger)
{
    // 📘 docs/06-repositorios-dapper-y-snapshots.md
    //
    // ¿POR QUÉ NO UN SIMPLE "CREATE TABLE IF NOT EXISTS"? Porque funciona una
    // sola vez. En cuanto agregues una columna, quien ya tenga el archivo .db
    // no la tendrá nunca (la tabla ya existe, así que el CREATE no hace nada) y
    // la aplicación fallará con "no such column".
    //
    // Este migrador mínimo hace lo que hacen las herramientas serias:
    //   1. Guarda en la tabla SchemaVersions qué migraciones ya se aplicaron.
    //   2. Aplica en orden solo las que faltan.
    //   3. Cada migración va en su propia transacción (SQLite permite DDL
    //      transaccional, así que una migración a medias no existe).
    //
    // Para agregar un cambio de esquema NUNCA edites una migración ya aplicada:
    // agrega una nueva al final del arreglo. Ese es todo el secreto.
    // En proyectos reales se usa DbUp o FluentMigrator, que son esto mismo con
    // más funciones (scripts en archivos, rollback, reportes).

    private static readonly (int Version, string Name, string Sql)[] Migrations =
    [
        (1, "Esquema inicial: productos, pedidos y líneas", InitialSchemaSql),
        (2, "Índice para buscar líneas por producto", "CREATE INDEX IF NOT EXISTS IX_OrderLines_ProductId ON OrderLines(ProductId);"),
    ];

    /// <summary>Aplica las migraciones pendientes.</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            CREATE TABLE IF NOT EXISTS SchemaVersions (
                Version   INTEGER NOT NULL PRIMARY KEY,
                Name      TEXT    NOT NULL,
                AppliedAt TEXT    NOT NULL
            );
            """,
            cancellationToken: cancellationToken));

        var appliedVersions = (await connection.QueryAsync<long>(new CommandDefinition(
            "SELECT Version FROM SchemaVersions",
            cancellationToken: cancellationToken))).ToHashSet();

        foreach (var (version, name, sql) in Migrations)
        {
            if (appliedVersions.Contains(version))
            {
                continue;
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO SchemaVersions (Version, Name, AppliedAt) VALUES (@Version, @Name, @AppliedAt)",
                new { Version = version, Name = name, AppliedAt = DateTimeOffset.UtcNow },
                transaction: transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Migración {Version} aplicada: {Name}", version, name);
        }

        // Después de tocar el esquema, la base deja inválidos los paquetes que
        // dependen de las tablas modificadas: se recompilan todos de una vez.
        await connection.ExecuteAsync(new CommandDefinition(
            "BEGIN PCK_ADMIN.SP_COMPILAR_INVALIDOS; END;",
            cancellationToken: cancellationToken));
    }

    // DECISIONES DE MAPEO DE TIPOS (SQLite solo tiene TEXT, INTEGER, REAL y BLOB):
    //   Guid           -> TEXT   (legible y portable)
    //   decimal        -> TEXT   (REAL es binario y PIERDE precisión con dinero;
    //                             lo dice la documentación del propio proveedor)
    //   DateTimeOffset -> TEXT   (ISO-8601, ordenable alfabéticamente)
    //   enum           -> TEXT   (se lee en la base sin tener que traducir números)
    //
    // Y dos reglas de integridad que NO se delegan a la aplicación:
    //   - UNIQUE en Sku: última línea de defensa contra dos peticiones
    //     simultáneas creando el mismo SKU.
    //   - CHECK (Stock >= 0): si alguien edita la base a mano, la base se niega.
    //
    // Fíjate en que OrderLines tiene clave foránea a Orders (están DENTRO del
    // mismo agregado) pero NO a Products: entre agregados distintos se
    // referencia por Id, sin integridad referencial de base de datos, para que
    // cada uno pueda evolucionar (o incluso vivir en otra base) por su cuenta.
    private const string InitialSchemaSql =
        """
        CREATE TABLE Products (
            Id            TEXT    NOT NULL PRIMARY KEY,
            Sku           TEXT    NOT NULL UNIQUE,
            Name          TEXT    NOT NULL,
            PriceAmount   TEXT    NOT NULL,
            PriceCurrency TEXT    NOT NULL,
            Stock         INTEGER NOT NULL CHECK (Stock >= 0),
            CreatedAt     TEXT    NOT NULL,
            UpdatedAt     TEXT    NULL,
            Version       INTEGER NOT NULL
        );

        CREATE TABLE Orders (
            Id          TEXT    NOT NULL PRIMARY KEY,
            Currency    TEXT    NOT NULL,
            Status      TEXT    NOT NULL,
            CreatedAt   TEXT    NOT NULL,
            PlacedAt    TEXT    NULL,
            CancelledAt TEXT    NULL,
            Version     INTEGER NOT NULL
        );

        CREATE TABLE OrderLines (
            Id        TEXT    NOT NULL PRIMARY KEY,
            OrderId   TEXT    NOT NULL REFERENCES Orders(Id) ON DELETE CASCADE,
            -- Posición de la línea dentro del pedido. Sin ella, el orden de las
            -- líneas al leerlas dependería del motor, y el agregado no volvería
            -- exactamente igual a como se guardó.
            Position  INTEGER NOT NULL,
            ProductId TEXT    NOT NULL,
            UnitPrice TEXT    NOT NULL,
            Quantity  INTEGER NOT NULL CHECK (Quantity > 0)
        );

        CREATE INDEX IX_OrderLines_OrderId ON OrderLines(OrderId);
        CREATE INDEX IX_Products_CreatedAt ON Products(CreatedAt);
        """;
}
