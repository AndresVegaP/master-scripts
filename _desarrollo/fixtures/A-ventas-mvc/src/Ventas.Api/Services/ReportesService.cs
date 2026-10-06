using Ventas.Api.Data.Interfaces;
using Ventas.Api.Models;

namespace Ventas.Api.Services;

public interface IReportesService
{
    Task<int> GenerarAsync(SolicitudReporte solicitud);
    Task<IEnumerable<VentaMensual>> VentasMensualesAsync(int anio);
}

public class ReportesService : IReportesService
{
    private readonly IReportesRepository _reportesRepository;

    public ReportesService(IReportesRepository reportesRepository)
    {
        _reportesRepository = reportesRepository;
    }

    public Task<int> GenerarAsync(SolicitudReporte solicitud)
        => _reportesRepository.GenerarAsync(solicitud.Tipo, solicitud.Desde, solicitud.Hasta);

    public Task<IEnumerable<VentaMensual>> VentasMensualesAsync(int anio)
        => _reportesRepository.VentasMensualesAsync(anio);
}
