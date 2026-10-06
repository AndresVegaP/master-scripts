using System.Data;
using System.Data.Common;
using Dapper;

namespace CleanArchitecture.Infrastructure.Persistence;

/// <summary>
/// Conexión (y transacción, si la hay) que comparten todos los repositorios
/// durante una misma petición.
/// </summary>
internal sealed class DbSession(IDbConnectionFactory connectionFactory) : IAsyncDisposable
{
    // 📘 docs/10-transacciones-y-concurrencia.md
    //
    // ¿POR QUÉ EXISTE ESTA CLASE? Para que dos repositorios puedan participar
    // en la MISMA transacción. Si cada método abriera su propia conexión, como
    // suele verse en los ejemplos sencillos, sería imposible guardar un pedido
    // y descontar el stock de sus productos de forma atómica.
    //
    // Se registra como Scoped: una sesión por petición HTTP. Al terminar la
    // petición, el contenedor de dependencias la libera, y si quedó una
    // transacción sin confirmar, se deshace. Ese es el comportamiento seguro:
    // lo que no se confirma explícitamente, no se guarda.

    private DbConnection? _connection;
    private DbTransaction? _transaction;

    /// <summary>Transacción activa, o null si las operaciones van sueltas.</summary>
    public DbTransaction? Transaction => _transaction;

    /// <summary>Devuelve la conexión de esta petición, abriéndola la primera vez.</summary>
    public async Task<DbConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        // Apertura perezosa: una petición que no toca la base de datos no abre
        // ninguna conexión.
        _connection ??= await connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return _connection;
    }

    /// <summary>
    /// Ejecuta un procedimiento almacenado por su nombre completo
    /// (PAQUETE.PROCEDIMIENTO), dentro de la transacción activa si la hay.
    /// </summary>
    /// <param name="spName">Nombre del procedimiento, con su paquete.</param>
    /// <param name="parameters">Parámetros (anónimos o DynamicParameters para los OUT).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    public async Task<int> ExecuteSpAsync(string spName, object? parameters, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(
            spName,
            parameters,
            _transaction,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    /// <summary>Abre una transacción para las siguientes operaciones.</summary>
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("Ya hay una transacción abierta en esta petición.");
        }

        var connection = await GetConnectionAsync(cancellationToken);
        _transaction = await connection.BeginTransactionAsync(cancellationToken);
    }

    /// <summary>Confirma la transacción abierta.</summary>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("No hay ninguna transacción abierta que confirmar.");
        }

        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            // Nadie confirmó: la petición falló o devolvió un error de negocio.
            // Deshacer es lo correcto y hace que olvidar el Commit sea seguro.
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
