using System.Data;
using Dapper;
using Dapper.Oracle;
using Ventas.Api.Models;

namespace Ventas.Api.Data.Repositories;

// Parte 2 del repositorio de ventas: consultas de solo lectura
public partial class VentasRepository
{
    // Sobrecarga con detalle: devuelve cabecera + lineas en dos cursores
    public async Task<VentaDetalle?> ObtenerVentaAsync(int id, bool incluirDetalle)
    {
        var p = new OracleDynamicParameters();
        p.Add("pIdVenta", id, OracleMappingType.Int32, ParameterDirection.Input);
        p.Add("pIncluirDetalle", incluirDetalle ? 1 : 0, OracleMappingType.Int32, ParameterDirection.Input);
        p.Add("pCurCabecera", dbType: OracleMappingType.RefCursor, direction: ParameterDirection.Output);
        p.Add("pCurDetalle", dbType: OracleMappingType.RefCursor, direction: ParameterDirection.Output);

        using var conn = CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            $"{StoredProcedures.PaqueteVentas}.SP_OBTENER_VENTA_DETALLE",
            p,
            commandType: CommandType.StoredProcedure);

        var venta = await multi.ReadFirstOrDefaultAsync<VentaDetalle>();
        if (venta is not null)
        {
            venta.Lineas = (await multi.ReadAsync<LineaVenta>()).ToList();
        }
        return venta;
    }

    public async Task<IEnumerable<ResumenDiario>> ObtenerResumenAsync(DateTime fecha)
    {
        const string sql = @"
            SELECT TRUNC(v.FECHA)                             AS DIA,
                   COUNT(*)                                   AS CANTIDAD,
                   SUM(PCK_VENTAS.FN_TOTAL_VENTA(v.ID_VENTA)) AS TOTAL
              FROM VENTAS v
             WHERE v.FECHA >= TRUNC(:fecha) - 30
               AND v.ESTADO <> 'ANULADA'
             GROUP BY TRUNC(v.FECHA)
             ORDER BY DIA";

        using var conn = CreateConnection();
        return await conn.QueryAsync<ResumenDiario>(sql, new { fecha });
    }
}
