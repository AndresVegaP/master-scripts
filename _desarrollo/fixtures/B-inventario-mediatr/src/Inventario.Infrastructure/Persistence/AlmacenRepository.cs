using System.Data;
using Dapper;
using Inventario.Application.Abstractions;
using Inventario.Application.Modelos;
using Inventario.Infrastructure.Constantes;

namespace Inventario.Infrastructure.Persistence;

public sealed class AlmacenRepository : Repository<Almacen>, IAlmacenRepository
{
    public AlmacenRepository(IDbConnection conexion, IDbTransaction? transaccion)
        : base(conexion, transaccion)
    {
    }

    protected override string NombreTabla => "ALMACENES";

    public async Task<IReadOnlyList<Almacen>> ListarAsync()
    {
        // Antes: PCK_ALMACEN.SP_LISTAR_ALMACENES devolvia un REF CURSOR; ahora es consulta directa
        const string sql = """
            SELECT CODIGO AS Codigo, NOMBRE AS Nombre, ESTADO AS Estado
              FROM ALMACENES
             WHERE ESTADO <> 'X'
             ORDER BY NOMBRE
            """;
        var filas = await QueryAsync<Almacen>(sql);
        return filas.ToList();
    }

    public Task ReservarAsync(string codigo, Guid productoId, decimal cantidad)
    {
        var p = new DynamicParameters();
        p.Add("p_almacen", codigo);
        p.Add("p_producto_id", productoId.ToString("N"));
        p.Add("p_cantidad", cantidad);
        return ExecuteSpAsync(Procedimientos.ReservarStock, p);
    }

    public Task EjecutarProcesoAsync(string nombreSp, string codigo)
    {
        var p = new DynamicParameters();
        p.Add("p_almacen", codigo);
        return ExecuteSpAsync(nombreSp, p);
    }
}
