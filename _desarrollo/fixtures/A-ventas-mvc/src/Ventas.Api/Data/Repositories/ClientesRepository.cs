using Dapper;
using Ventas.Api.Data.Interfaces;
using Ventas.Api.Models;

namespace Ventas.Api.Data.Repositories;

/// <summary>
/// Acceso a datos de clientes.
/// Reemplaza a PCK_CLIENTES.SP_LISTAR_CLIENTES y PCK_CLIENTES.SP_OBTENER_CLIENTE.
/// </summary>
public class ClientesRepository : BaseRepository, IClientesRepository
{
    // Migrado de PCK_CLIENTES.SP_LISTAR_CLIENTES
    // Ojo: el saldo y el formato del RUT se siguen calculando en Oracle
    private const string SqlListarClientes = @"
        SELECT c.ID_CLIENTE,
               c.RAZON_SOCIAL,
               REPLACE(c.OBSERVACION, '/*', ' ')           AS OBSERVACION,
               PCK_UTIL.FN_FORMATEAR_RUT(c.RUT)            AS RUT,
               PCK_CLIENTES.FN_SALDO_CLIENTE(c.ID_CLIENTE) AS SALDO
          FROM CLIENTES c
         WHERE c.ACTIVO = 'S'
         ORDER BY c.RAZON_SOCIAL";

    public ClientesRepository(IConfiguration configuration) : base(configuration)
    {
    }

    public async Task<IEnumerable<Cliente>> ListarAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<Cliente>(SqlListarClientes);
    }

    public async Task<Cliente?> ObtenerAsync(int id)
    {
        var sql = "SELECT ID_CLIENTE, RAZON_SOCIAL, RUT, EMAIL FROM CLIENTES WHERE ID_CLIENTE = :id"; // Migrado de PCK_CLIENTES.SP_OBTENER_CLIENTE
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Cliente>(sql, new { id });
    }

    public async Task<IEnumerable<Cliente>> BuscarAsync(string texto)
    {
        // Busqueda insensible a tildes y mayusculas
        const string sql = @"
            SELECT c.ID_CLIENTE, c.RAZON_SOCIAL, c.RUT
              FROM CLIENTES c
             WHERE NVL(c.RAZON_SOCIAL, '--') <> '--' AND PCK_UTIL.FN_NORMALIZAR(c.RAZON_SOCIAL) LIKE '%' || PCK_UTIL.FN_NORMALIZAR(:texto) || '%'
             ORDER BY c.RAZON_SOCIAL";

        using var conn = CreateConnection();
        return await conn.QueryAsync<Cliente>(sql, new { texto });
    }

    public Task<int> EliminarInactivosAsync(int diasInactividad)
    {
        return ExecuteSpAsync("PCK_CLIENTES.SP_ELIMINAR_INACTIVOS", new { pDias = diasInactividad });
    }
}
