using CleanArchitecture.Application.Reports.MonthlyClose;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.Persistence.Reports;

/// <summary>Lanza procesos batch que siguen viviendo en la base de datos.</summary>
internal sealed class ProcesosBatchRepository(DbSession session, IOptions<ProcesosBatchOptions> options) : IProcesosBatch
{
    public async Task EjecutarCierreMensualAsync(DateOnly periodo, CancellationToken cancellationToken = default)
    {
        // El nombre del procedimiento depende del ambiente (en QA apunta a una
        // copia del paquete), así que se lee de la configuración.
        var procedimiento = options.Value.CierreMensual;

        if (string.IsNullOrWhiteSpace(procedimiento))
        {
            throw new InvalidOperationException("Falta configurar 'ProcesosBatch:CierreMensual'.");
        }

        await session.ExecuteSpAsync(
            procedimiento,
            new { p_anio = periodo.Year, p_mes = periodo.Month },
            cancellationToken);
    }
}

/// <summary>Configuración de los procesos batch (sección "ProcesosBatch").</summary>
internal sealed class ProcesosBatchOptions
{
    /// <summary>Procedimiento de cierre mensual, con su paquete. Se define por ambiente.</summary>
    public string CierreMensual { get; set; } = string.Empty;
}
