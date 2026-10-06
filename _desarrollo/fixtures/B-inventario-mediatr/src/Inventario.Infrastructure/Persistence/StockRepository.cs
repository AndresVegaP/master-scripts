using System.Data;
using Dapper;
using Inventario.Application.Abstractions;
using Inventario.Application.Modelos;
using Inventario.Infrastructure.Constantes;
using Microsoft.Extensions.Logging;

namespace Inventario.Infrastructure.Persistence;

public sealed class StockRepository : Repository<StockDto>, IStockRepository
{
    private readonly ILogger<StockRepository> _logger;

    public StockRepository(IDbConnection conexion, IDbTransaction? transaccion, ILogger<StockRepository> logger)
        : base(conexion, transaccion)
    {
        _logger = logger;
    }

    protected override string NombreTabla => "STOCK_PRODUCTO";

    public async Task<decimal> AjustarAsync(Guid productoId, decimal cantidad, string motivo)
    {
        var p = new DynamicParameters();
        p.Add("p_producto_id", productoId.ToString("N"));
        p.Add("p_cantidad", cantidad);
        p.Add("p_motivo", motivo);
        p.Add("p_saldo", dbType: DbType.Decimal, direction: ParameterDirection.Output);

        try
        {
            await Connection.ExecuteAsync($"{Paquetes.Inventario}.SP_AJUSTAR_STOCK", p, Transaction,
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo PCK_INVENTARIO.SP_AJUSTAR_STOCK para el producto {ProductoId}", productoId);
            throw;
        }

        return p.Get<decimal>("p_saldo");
    }

    public Task<decimal> ObtenerStockMinimoAsync(Guid productoId)
        => Connection.ExecuteScalarAsync<decimal>(
            "SELECT PCK_INVENTARIO.FN_STOCK_MINIMO@DBL_CENTRAL(:productoId) FROM DUAL",
            new { productoId = productoId.ToString("N") }, Transaction);

    public async Task<StockDto?> ConsultarPorSkuAsync(string sku)
    {
        var sql = """
            /* Paquete: PCK_INVENTARIO - SP: SP_CONSULTAR_STOCK */
            SELECT s.SKU        AS Sku,
                   s.DISPONIBLE AS Disponible,
                   s.RESERVADO  AS Reservado,
                   CASE WHEN s.DISPONIBLE <= 0 THEN 'SIN STOCK' ELSE 'OK' END AS Estado
              FROM STOCK_PRODUCTO s
             WHERE s.SKU = :sku
            """;
        return await Connection.QuerySingleOrDefaultAsync<StockDto>(sql, new { sku }, Transaction);
    }

    public Task RecalcularDisponibleAsync(Guid productoId)
        => Connection.ExecuteAsync("CALL PCK_INVENTARIO.SP_RECALCULAR_DISPONIBLE(:productoId)",
            new { productoId = productoId.ToString("N") }, Transaction);
}
