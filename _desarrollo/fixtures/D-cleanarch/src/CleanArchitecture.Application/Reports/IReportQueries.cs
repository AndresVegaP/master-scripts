namespace CleanArchitecture.Application.Reports;

/// <summary>Puerto de LECTURA de los reportes.</summary>
public interface IReportQueries
{
    // Mismo enfoque que IProductQueries: SQL directo al DTO de la pantalla, sin
    // pasar por agregados. Nadie va a modificar nada desde un reporte.

    /// <summary>Ventas del año agrupadas por mes.</summary>
    Task<IReadOnlyList<VentaMensualDto>> GetVentasPorMesAsync(int anio, CancellationToken cancellationToken = default);

    /// <summary>Productos con stock disponible menor que el umbral.</summary>
    Task<IReadOnlyList<StockBajoDto>> GetStockBajoAsync(int umbral, CancellationToken cancellationToken = default);

    /// <summary>Importe con el símbolo y los decimales de la moneda. Null si la moneda no existe.</summary>
    Task<string?> FormatearPrecioAsync(decimal monto, string moneda, CancellationToken cancellationToken = default);

    /// <summary>Ranking de los productos más vendidos.</summary>
    Task<IReadOnlyList<TopProductoDto>> GetTopProductosAsync(CancellationToken cancellationToken = default);

    /// <summary>Tipo de cambio del día para la moneda.</summary>
    Task<decimal> GetTipoCambioAsync(string moneda, CancellationToken cancellationToken = default);

    /// <summary>Monedas admitidas.</summary>
    Task<IReadOnlyList<MonedaDto>> ListarMonedasAsync(CancellationToken cancellationToken = default);
}
