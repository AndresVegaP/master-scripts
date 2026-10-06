namespace CleanArchitecture.Application.Reports;

// Casos de uso de LECTURA de reportes. Son tan finos (normalizar la entrada y
// delegar en el puerto) que viven juntos en un archivo; si alguno crece, se
// muda a su propia carpeta como los de Products y Orders.

/// <summary>Caso de uso: ventas del año por mes.</summary>
public sealed class SalesReportHandler(IReportQueries reportQueries)
{
    /// <summary>Ejecuta el caso de uso.</summary>
    public Task<IReadOnlyList<VentaMensualDto>> ExecuteAsync(int anio, CancellationToken cancellationToken = default) =>
        reportQueries.GetVentasPorMesAsync(anio, cancellationToken);
}

/// <summary>Caso de uso: productos con poco stock.</summary>
public sealed class LowStockHandler(IReportQueries reportQueries)
{
    /// <summary>Umbral que se usa cuando la petición no trae uno.</summary>
    public const int UmbralPorDefecto = 10;

    /// <summary>Ejecuta el caso de uso.</summary>
    public Task<IReadOnlyList<StockBajoDto>> ExecuteAsync(int umbral, CancellationToken cancellationToken = default) =>
        reportQueries.GetStockBajoAsync(Math.Max(umbral, 0), cancellationToken);
}

/// <summary>Caso de uso: formatear un importe según su moneda.</summary>
public sealed class FormatPriceHandler(IReportQueries reportQueries)
{
    /// <summary>Ejecuta el caso de uso.</summary>
    public Task<string?> ExecuteAsync(decimal monto, string moneda, CancellationToken cancellationToken = default) =>
        reportQueries.FormatearPrecioAsync(monto, moneda.ToUpperInvariant(), cancellationToken);
}

/// <summary>Caso de uso: ranking de productos más vendidos.</summary>
public sealed class TopProductsHandler(IReportQueries reportQueries)
{
    /// <summary>Ejecuta el caso de uso.</summary>
    public Task<IReadOnlyList<TopProductoDto>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        reportQueries.GetTopProductosAsync(cancellationToken);
}

/// <summary>Caso de uso: tipo de cambio del día.</summary>
public sealed class ExchangeRateHandler(IReportQueries reportQueries)
{
    /// <summary>Ejecuta el caso de uso.</summary>
    public Task<decimal> ExecuteAsync(string moneda, CancellationToken cancellationToken = default) =>
        reportQueries.GetTipoCambioAsync(moneda.ToUpperInvariant(), cancellationToken);
}

/// <summary>Caso de uso: monedas admitidas.</summary>
public sealed class ListCurrenciesHandler(IReportQueries reportQueries)
{
    /// <summary>Ejecuta el caso de uso.</summary>
    public Task<IReadOnlyList<MonedaDto>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        reportQueries.ListarMonedasAsync(cancellationToken);
}
