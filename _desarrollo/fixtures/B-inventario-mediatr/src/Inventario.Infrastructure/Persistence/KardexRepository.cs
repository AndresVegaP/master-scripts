using System.Data;
using Inventario.Application.Abstractions;
using Inventario.Application.Modelos;

namespace Inventario.Infrastructure.Persistence;

public sealed class KardexRepository : Repository<MovimientoKardexDto>, IKardexRepository
{
    public KardexRepository(IDbConnection conexion, IDbTransaction? transaccion)
        : base(conexion, transaccion)
    {
    }

    protected override string NombreTabla => "MOVIMIENTOS_STOCK";

    public async Task<IReadOnlyList<MovimientoKardexDto>> ListarMovimientosAsync(Guid productoId, int anio)
    {
        var sql = """
            SELECT m.FECHA     AS Fecha,
                   m.TIPO      AS Tipo,
                   m.CANTIDAD  AS Cantidad,
                   0           AS Saldo,
                   m.DOCUMENTO AS Documento
              FROM MOVIMIENTOS_STOCK m
             WHERE m.PRODUCTO_ID = :productoId
               AND EXTRACT(YEAR FROM m.FECHA) = :anio
             ORDER BY m.FECHA, m.ID
            """;
        var filas = await QueryAsync<MovimientoKardexDto>(sql, new { productoId = productoId.ToString("N"), anio });
        return filas.ToList();
    }
}
