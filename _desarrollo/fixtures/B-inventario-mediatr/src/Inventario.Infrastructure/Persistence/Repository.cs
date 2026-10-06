using System.Data;
using Dapper;

namespace Inventario.Infrastructure.Persistence;

/// <summary>
/// Base generica para repositorios Dapper. Expone helpers comunes { consulta, ejecucion de procedimientos }.
/// </summary>
public abstract class Repository<T> where T : class
{
    protected Repository(IDbConnection conexion, IDbTransaction? transaccion)
    {
        Connection = conexion;
        Transaction = transaccion;
    }

    protected IDbConnection Connection { get; }

    protected IDbTransaction? Transaction { get; }

    /// <summary>Tabla principal del repositorio (para las consultas genericas).</summary>
    protected abstract string NombreTabla { get; }

    public virtual Task<T?> ObtenerPorCodigoAsync(string codigo)
        => Connection.QuerySingleOrDefaultAsync<T>(
            $"SELECT * FROM {NombreTabla} WHERE CODIGO = :codigo", new { codigo }, Transaction);

    protected Task<IEnumerable<TResult>> QueryAsync<TResult>(string sql, object? parametros = null)
        => Connection.QueryAsync<TResult>(sql, parametros, Transaction);

    protected Task<int> ExecuteSpAsync(string spName, DynamicParameters parametros)
        => Connection.ExecuteAsync(spName, parametros, Transaction, commandType: CommandType.StoredProcedure);
}
