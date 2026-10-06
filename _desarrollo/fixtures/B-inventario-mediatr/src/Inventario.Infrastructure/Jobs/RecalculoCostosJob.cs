using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Inventario.Infrastructure.Jobs;

/// <summary>
/// Job nocturno de recalculo de costo promedio. Todavia no se registra en el host
/// (pendiente decidir entre Hangfire y Quartz).
/// </summary>
public sealed class RecalculoCostosJob
{
    private readonly IDbConnection _conexion;
    private readonly ILogger<RecalculoCostosJob> _logger;

    public RecalculoCostosJob(IDbConnection conexion, ILogger<RecalculoCostosJob> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task EjecutarAsync(DateTime fechaCorte)
    {
        _logger.LogInformation("Iniciando recalculo de costos al {FechaCorte:yyyy-MM-dd}", fechaCorte);
        await _conexion.ExecuteAsync("PCK_COSTOS.SP_RECALCULAR_COSTO_PROMEDIO",
            new { p_fecha_corte = fechaCorte }, commandType: CommandType.StoredProcedure);
    }
}
