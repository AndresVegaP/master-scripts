using System.Data;
using Dapper;
using Dapper.Oracle;
using Oracle.ManagedDataAccess.Client;

namespace Ventas.Api.Data;

/// <summary>
/// Clase base de los repositorios: maneja la conexion y los wrappers para ejecutar procedimientos.
/// </summary>
public abstract class BaseRepository
{
    private readonly string _connectionString;

    protected BaseRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Oracle")
            ?? throw new InvalidOperationException("Falta la cadena de conexion 'Oracle'");
    }

    protected IDbConnection CreateConnection() => new OracleConnection(_connectionString);

    // Ejecuta un procedimiento sin cursores de salida (INSERT/UPDATE/DELETE)
    protected async Task<int> ExecuteSpAsync(string spName, object param)
    {
        using var conn = CreateConnection();
        return await conn.ExecuteAsync(spName, param, commandType: CommandType.StoredProcedure);
    }

    // Ejecuta un procedimiento que devuelve un SYS_REFCURSOR (parametro de salida "pCursor")
    protected async Task<IEnumerable<T>> QuerySpAsync<T>(string spName, OracleDynamicParameters param)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<T>(spName, param, commandType: CommandType.StoredProcedure);
    }
}
