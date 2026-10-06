using System.Data;
using CleanArchitecture.Application.Reports;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Infrastructure.Persistence.Reports;

/// <summary>Adaptador de lectura de los reportes.</summary>
internal sealed class ReportQueries(DbSession session, ILogger<ReportQueries> logger)
    : OracleQueryBase(session), IReportQueries
{
    // Cada reporte se migra por separado: algunos ya son SQL puro y otros
    // todavía llaman a funciones o procedimientos de la base. Regla del equipo:
    // el comentario "Migrado de ..." va pegado al SQL que reemplaza al original.

    // Ficha funcional de los reportes. Ojo: la URL termina en /* porque el wiki
    // usa ese comodín; no es el inicio de un comentario.
    private const string FichaFuncional = "https://intranet.empresa.local/wiki/reportes/*";

    public async Task<IReadOnlyList<VentaMensualDto>> GetVentasPorMesAsync(int anio, CancellationToken cancellationToken = default)
    {
        var connection = await Session.GetConnectionAsync(cancellationToken);

        // El SQL vive en un .sql incrustado en el ensamblado: es largo y el DBA
        // lo revisa con sus propias herramientas.
        var sql = SqlResources.Load("Queries.Reportes.VentasPorMes.sql");

        var rows = await connection.QueryAsync<VentaMensualDto>(new CommandDefinition(
            sql,
            new { Anio = anio },
            Session.Transaction,
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    public async Task<IReadOnlyList<StockBajoDto>> GetStockBajoAsync(int umbral, CancellationToken cancellationToken = default)
    {
        var connection = await Session.GetConnectionAsync(cancellationToken);

        // El reporte viejo armaba "{sku} - {nombre" en una sola columna (le
        // faltaba la llave de cierre y salía cortado); ahora van separadas.
        var rows = await connection.QueryAsync<StockBajoDto>(new CommandDefinition(
            """
            SELECT x.Sku, x.Name, x.Nota, x.Disponible
            FROM (
                SELECT p.Sku, p.Name,
                       NVL(p.Nota, '-- sin nota --') AS Nota, "PCK_INVENTARIO"."FN_STOCK_DISPONIBLE"(p.Id) AS Disponible
                FROM Products p
            ) x
            /* disponible = stock físico menos reservas de pedidos en borrador */
            WHERE x.Disponible < :Umbral
            ORDER BY x.Disponible, x.Sku
            """,
            new { Umbral = umbral },
            Session.Transaction,
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    public async Task<string?> FormatearPrecioAsync(decimal monto, string moneda, CancellationToken cancellationToken = default)
    {
        var connection = await Session.GetConnectionAsync(cancellationToken);

        var sql = "SELECT PCK_UTIL.FN_SIMBOLO_MONEDA(m.Codigo) || ' ' || TO_CHAR(PCK_UTIL.FN_REDONDEAR(:Monto, m.Decimales)) FROM Monedas m WHERE m.Codigo = :Moneda"; // Migrado de PCK_UTIL.FN_FORMATEAR_PRECIO

        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            sql,
            new { Monto = monto, Moneda = moneda },
            Session.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TopProductoDto>> GetTopProductosAsync(CancellationToken cancellationToken = default)
    {
        var connection = await Session.GetConnectionAsync(cancellationToken);

        var parameters = new OracleDynamicParameters();
        parameters.Add("p_dias", 30);
        parameters.AddRefCursor("p_cursor");

        try
        {
            var rows = await connection.QueryAsync<TopProductoDto>(new CommandDefinition(
                "PCK_REPORTES.SP_TOP_PRODUCTOS",
                parameters,
                Session.Transaction,
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

            return [.. rows];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Error ejecutando PCK_REPORTES.SP_TOP_PRODUCTOS (ver {Ficha})", FichaFuncional);
            throw new InvalidOperationException("No se pudo obtener el ranking desde PCK_REPORTES.SP_TOP_PRODUCTOS.", exception);
        }
    }

    // Tipo de cambio: la función del paquete de utilidades lee la tabla de
    // cotizaciones del día y aplica el redondeo contable.
    public Task<decimal> GetTipoCambioAsync(string moneda, CancellationToken cancellationToken = default) =>
        EjecutarFuncionAsync<decimal>("PCK_UTIL.FN_TIPO_CAMBIO", moneda, cancellationToken);

    public async Task<IReadOnlyList<MonedaDto>> ListarMonedasAsync(CancellationToken cancellationToken = default)
    {
        var connection = await Session.GetConnectionAsync(cancellationToken);

        // La columna de origen guarda QUÉ proceso dio de alta la moneda: es un
        // dato, no una llamada. Las que creó el proceso legado de bajas no salen.
        var rows = await connection.QueryAsync<MonedaDto>(new CommandDefinition(
            """
            SELECT m.Codigo, m.Decimales, m.SP_ORIGEN AS ProcesoOrigen
            FROM Monedas m
            WHERE NVL(m.SP_ORIGEN, 'MANUAL') <> 'PCK_LEGADO.SP_BAJA_MONEDA'
            ORDER BY m.Codigo
            """,
            null,
            Session.Transaction,
            cancellationToken: cancellationToken));

        return [.. rows];
    }
}
