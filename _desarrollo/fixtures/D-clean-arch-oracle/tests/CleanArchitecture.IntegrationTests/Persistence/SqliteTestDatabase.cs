using CleanArchitecture.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleanArchitecture.IntegrationTests.Persistence;

/// <summary>Base de datos SQLite en memoria, limpia y aislada para cada test.</summary>
public abstract class SqliteTestDatabase : IAsyncLifetime
{
    // 📘 docs/09-tests.md
    //
    // TÉCNICA: "Data Source={nombre};Mode=Memory;Cache=Shared" crea una base de
    // datos SQLite EN MEMORIA con nombre, que varias conexiones del mismo
    // proceso pueden compartir. Vive mientras haya al menos UNA conexión
    // abierta: por eso mantenemos _keepAlive abierta durante todo el test.
    //
    // Como el nombre lleva un Guid, cada test tiene SU base: se pueden ejecutar
    // en paralelo sin pisarse y ninguno depende del estado que dejó otro.

    private SqliteConnection _keepAlive = null!;

    /// <summary>Sesión de base de datos que usan los repositorios bajo prueba.</summary>
    private protected DbSession Session { get; private set; } = null!;

    /// <summary>Fábrica de conexiones apuntando a la base de este test.</summary>
    private protected IDbConnectionFactory ConnectionFactory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(connectionString);
        await _keepAlive.OpenAsync(TestContext.Current.CancellationToken);

        // Usamos las MISMAS clases que en producción: solo cambia la cadena de
        // conexión. Cuanto más código real ejercite un test de integración,
        // más vale.
        ConnectionFactory = new SqliteConnectionFactory(connectionString);

        await new DatabaseMigrator(ConnectionFactory, NullLogger<DatabaseMigrator>.Instance)
            .MigrateAsync(TestContext.Current.CancellationToken);

        Session = new DbSession(ConnectionFactory);
    }

    public async ValueTask DisposeAsync()
    {
        await Session.DisposeAsync();
        await _keepAlive.DisposeAsync(); // al cerrarse la última conexión, la base desaparece
    }
}
