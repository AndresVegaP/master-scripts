using System.Data;
using System.Text;
using Dapper;
using Dapper.Oracle;
using Ventas.Api.Data.Interfaces;
using Ventas.Api.Models;

namespace Ventas.Api.Data.Repositories;

/// <summary>
/// Repositorio de ventas (parte 1: comandos y listados).
/// Las consultas de solo lectura estan en VentasRepository.Consultas.cs
/// </summary>
public partial class VentasRepository : BaseRepository, IVentasRepository
{
    public VentasRepository(IConfiguration configuration) : base(configuration)
    {
    }

    public async Task<IEnumerable<VentaResumen>> ListarAsync(DateTime? desde, DateTime? hasta)
    {
        // Migrado de PCK_VENTAS.SP_LISTAR_VENTAS
        // La consulta se arma segun los filtros; las ventas del proceso masivo no se muestran
        var sql = new StringBuilder();
        sql.AppendLine("SELECT v.ID_VENTA, v.FOLIO, v.FECHA, v.MONTO_TOTAL, c.RAZON_SOCIAL");
        sql.AppendLine("  FROM VENTAS v");
        sql.AppendLine("  JOIN CLIENTES c ON c.ID_CLIENTE = v.ID_CLIENTE");
        sql.AppendLine(" WHERE v.ESTADO <> 'ANULADA'");
        sql.AppendLine("   AND v.ORIGEN <> 'PCK_VENTAS.SP_IMPORTAR_VENTAS'");
        if (desde.HasValue)
        {
            sql.AppendLine("   AND v.FECHA >= :desde");
        }
        if (hasta.HasValue)
        {
            sql.AppendLine("   AND v.FECHA < :hasta + 1");
        }
        sql.AppendLine(" ORDER BY v.FECHA DESC");

        using var conn = CreateConnection();
        return await conn.QueryAsync<VentaResumen>(sql.ToString(), new { desde, hasta });
    }

    public async Task<Venta?> ObtenerVentaAsync(int id)
    {
        var p = new OracleDynamicParameters();
        p.Add("pIdVenta", id, OracleMappingType.Int32, ParameterDirection.Input);
        p.Add("pCursor", dbType: OracleMappingType.RefCursor, direction: ParameterDirection.Output);

        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Venta>(
            StoredProcedures.ObtenerVenta, p, commandType: CommandType.StoredProcedure);
    }

    public async Task<string> ObtenerSiguienteFolioAsync(string serie)
    {
        using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<string>(
            "SELECT PCK_VENTAS.FN_SIGUIENTE_FOLIO(:serie) FROM DUAL", new { serie });
    }

    public Task<int> RegistrarAsync(VentaDto dto)
    {
        return ExecuteSpAsync(StoredProcedures.RegistrarVenta, new
        {
            pFolio = dto.Folio,
            pIdCliente = dto.IdCliente,
            pMonto = dto.Monto,
            pFecha = dto.Fecha
        });
    }

    public async Task ActualizarAsync(int id, VentaDto dto, string usuario)
    {
        // Bloque anonimo: actualiza la venta y deja auditoria en la misma llamada
        const string sql = @"
            BEGIN
                PCK_VENTAS.SP_ACTUALIZAR_VENTA(:pIdVenta, :pMonto, :pEstado);
                PCK_AUDITORIA.SP_REGISTRAR_CAMBIO(:pIdVenta, 'VENTAS', :pUsuario);
            END;";

        using var conn = CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            pIdVenta = id,
            pMonto = dto.Monto,
            pEstado = dto.Estado,
            pUsuario = usuario
        });
    }

    public async Task AnularAsync(int id)
    {
        using var conn = CreateConnection();
        var filas = await conn.ExecuteAsync(
            "PCK_VENTAS.SP_ANULAR_VENTA",
            new { pIdVenta = id },
            commandType: CommandType.StoredProcedure);

        if (filas == 0)
        {
            throw new InvalidOperationException($"PCK_VENTAS.SP_ANULAR_VENTA no anulo la venta {{id={id}}}");
        }
    }

    public async Task SincronizarAsync(DateTime fecha)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("CALL PCK_INTEGRACION.SP_SINCRONIZAR_VENTAS(:pFecha)", new { pFecha = fecha });
    }

    // Solo lo usa el job nocturno
    public Task RecalcularTotalesAsync()
    {
        return ExecuteSpAsync("PCK_VENTAS.SP_RECALCULAR_TOTALES", new { pForzar = 1 });
    }
}
