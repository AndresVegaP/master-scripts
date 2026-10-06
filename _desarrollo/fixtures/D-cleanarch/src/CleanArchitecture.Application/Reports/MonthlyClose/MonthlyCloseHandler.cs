using CleanArchitecture.Application.Abstractions;

namespace CleanArchitecture.Application.Reports.MonthlyClose;

/// <summary>Caso de uso: lanzar el cierre contable del mes anterior.</summary>
public sealed class MonthlyCloseHandler(IProcesosBatch procesosBatch, TimeProvider timeProvider)
{
    /// <summary>Ejecuta el caso de uso.</summary>
    public async Task<Result> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // El cierre siempre es del mes ANTERIOR al de hoy: el mes en curso
        // todavía puede recibir pedidos.
        var hoy = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var periodo = new DateOnly(hoy.Year, hoy.Month, 1).AddMonths(-1);

        await procesosBatch.EjecutarCierreMensualAsync(periodo, cancellationToken);

        return Result.Success();
    }
}
