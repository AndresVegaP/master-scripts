using CleanArchitecture.Api.ErrorHandling;
using CleanArchitecture.Application.Reports;
using CleanArchitecture.Application.Reports.ClientSummary;
using CleanArchitecture.Application.Reports.MonthlyClose;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitecture.Api.Reports;

/// <summary>Endpoints HTTP de reportes.</summary>
public static class ReportEndpoints
{
    // Los reportes nacieron como procedimientos almacenados y se están migrando
    // de uno en uno. El endpoint no sabe (ni debe saber) si detrás hay SQL puro
    // o una llamada a la base de datos: eso es asunto de Infrastructure.
    //
    // Este archivo NO crea el prefijo "/api": lo recibe ya armado desde
    // Program.cs (api.MapReportEndpoints()). Aquí solo se agrega "/reports".

    /// <summary>Registra las rutas de reportes dentro del grupo recibido.</summary>
    public static RouteGroupBuilder MapReportEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var group = api.MapGroup("/reports").WithTags("Reportes");

        group.MapGet("/ventas/{anio:int}", GetSalesReport)
            .WithName(nameof(GetSalesReport));

        // "{umbral:int?}" es OPCIONAL: sin él se usa el umbral por defecto.
        group.MapGet("/stock-bajo/{umbral:int?}", GetLowStock)
            .WithName(nameof(GetLowStock));

        group.MapGet("/precio-formateado", FormatPrice)
            .WithName(nameof(FormatPrice));

        group.MapGet("/top-productos", GetTopProducts)
            .WithName(nameof(GetTopProducts));

        group.MapGet("/monedas", ListCurrencies)
            .WithName(nameof(ListCurrencies));

        group.MapPost("/cierre-mensual", RunMonthlyClose)
            .WithName(nameof(RunMonthlyClose))
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Sub-grupo: lo de clientes cuelga de "/api/reports/clientes".
        var clientes = group.MapGroup("/clientes");

        clientes.MapGet("/{clienteId:long}/resumen", GetClientSummary)
            .WithName(nameof(GetClientSummary));

        // Endpoint en línea (lambda): para algo tan corto no vale la pena un
        // método aparte. La restricción "alpha:length(3)" exige un código ISO.
        group.MapGet(
                "/tipo-cambio/{moneda:alpha:length(3)}",
                async (string moneda, ExchangeRateHandler handler, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await handler.ExecuteAsync(moneda, cancellationToken)))
            .WithName("GetExchangeRate");

        return api;
    }

    /// <summary>Ventas del año, agrupadas por mes.</summary>
    /// <param name="anio">Año del reporte.</param>
    public static async Task<Ok<IReadOnlyList<VentaMensualDto>>> GetSalesReport(
        int anio,
        SalesReportHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.ExecuteAsync(anio, cancellationToken));

    /// <summary>Productos con stock disponible por debajo del umbral.</summary>
    /// <param name="umbral">Unidades mínimas (por defecto, 10).</param>
    public static async Task<Ok<IReadOnlyList<StockBajoDto>>> GetLowStock(
        LowStockHandler handler,
        CancellationToken cancellationToken,
        int? umbral = null) =>
        TypedResults.Ok(await handler.ExecuteAsync(umbral ?? LowStockHandler.UmbralPorDefecto, cancellationToken));

    /// <summary>Formatea un importe con el símbolo y los decimales de su moneda.</summary>
    /// <param name="monto">Importe a formatear.</param>
    /// <param name="moneda">Código ISO de la moneda.</param>
    public static async Task<Results<Ok<string>, NotFound>> FormatPrice(
        [FromQuery] decimal monto,
        [FromQuery] string moneda,
        FormatPriceHandler handler,
        CancellationToken cancellationToken)
    {
        var texto = await handler.ExecuteAsync(monto, moneda, cancellationToken);

        return texto is null ? TypedResults.NotFound() : TypedResults.Ok(texto);
    }

    /// <summary>Los productos más vendidos de los últimos 30 días.</summary>
    public static async Task<Ok<IReadOnlyList<TopProductoDto>>> GetTopProducts(
        TopProductsHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.ExecuteAsync(cancellationToken));

    /// <summary>Monedas admitidas y sus decimales.</summary>
    public static async Task<Ok<IReadOnlyList<MonedaDto>>> ListCurrencies(
        ListCurrenciesHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.ExecuteAsync(cancellationToken));

    /// <summary>Lanza el cierre contable del mes anterior.</summary>
    /// <remarks>El proceso corre en la base de datos y puede tardar varios minutos.</remarks>
    /// <response code="202">Cierre lanzado.</response>
    [EndpointDescription("Package: PCK_REPORTES - SP: SP_CIERRE_MENSUAL")]
    public static async Task<Results<Accepted, ProblemHttpResult>> RunMonthlyClose(
        MonthlyCloseHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.ExecuteAsync(cancellationToken);

        return result.IsSuccess
            ? TypedResults.Accepted((string?)null)
            : result.Error.ToProblem(httpContext);
    }

    /// <summary>Resumen de compras de un cliente.</summary>
    /// <param name="clienteId">Identificador del cliente.</param>
    public static async Task<Results<Ok<ResumenClienteDto>, NotFound>> GetClientSummary(
        long clienteId,
        ClientSummaryHandler handler,
        CancellationToken cancellationToken)
    {
        var resumen = await handler.ExecuteAsync(clienteId, cancellationToken);

        return resumen is null ? TypedResults.NotFound() : TypedResults.Ok(resumen);
    }
}
