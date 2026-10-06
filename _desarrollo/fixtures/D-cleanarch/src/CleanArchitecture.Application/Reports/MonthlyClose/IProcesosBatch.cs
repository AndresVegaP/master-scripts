namespace CleanArchitecture.Application.Reports.MonthlyClose;

/// <summary>Puerto para lanzar procesos batch de la base de datos.</summary>
public interface IProcesosBatch
{
    /// <summary>Lanza el cierre contable del período (primer día del mes).</summary>
    Task EjecutarCierreMensualAsync(DateOnly periodo, CancellationToken cancellationToken = default);
}
