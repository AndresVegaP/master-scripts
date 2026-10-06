using Ventas.Api.Data.Interfaces;
using Ventas.Api.Models;

namespace Ventas.Api.Services;

public interface IVentasService
{
    Task<IEnumerable<VentaResumen>> ListarAsync(DateTime? desde, DateTime? hasta);
    Task<Venta?> ObtenerAsync(int id);
    Task<VentaDetalle?> ObtenerConDetalleAsync(int id);
    Task<int> RegistrarAsync(VentaDto dto);
    Task ActualizarAsync(int id, VentaDto dto, string usuario);
    Task AnularAsync(int id);
    Task SincronizarAsync(DateTime fecha);
    Task<IEnumerable<ResumenDiario>> ObtenerResumenAsync(DateTime fecha);
    Task<IEnumerable<MedioPago>> ListarMediosPagoAsync();
    Task RecalcularTotalesAsync();
}

public class VentasService : IVentasService
{
    private readonly IVentasRepository _ventasRepository;
    private readonly IParametrosRepository _parametrosRepository;
    private readonly ILogger<VentasService> _logger;

    public VentasService(
        IVentasRepository ventasRepository,
        IParametrosRepository parametrosRepository,
        ILogger<VentasService> logger)
    {
        _ventasRepository = ventasRepository;
        _parametrosRepository = parametrosRepository;
        _logger = logger;
    }

    public Task<IEnumerable<VentaResumen>> ListarAsync(DateTime? desde, DateTime? hasta)
        => _ventasRepository.ListarAsync(desde, hasta);

    public Task<Venta?> ObtenerAsync(int id)
        => _ventasRepository.ObtenerVentaAsync(id);

    public Task<VentaDetalle?> ObtenerConDetalleAsync(int id)
        => _ventasRepository.ObtenerVentaAsync(id, incluirDetalle: true);

    public async Task<int> RegistrarAsync(VentaDto dto)
    {
        // Primero se reserva el folio y luego se graba la venta
        dto.Folio = await _ventasRepository.ObtenerSiguienteFolioAsync(dto.Serie);
        _logger.LogInformation("Registrando venta {Folio} del cliente {IdCliente}", dto.Folio, dto.IdCliente);
        return await _ventasRepository.RegistrarAsync(dto);
    }

    public Task ActualizarAsync(int id, VentaDto dto, string usuario)
        => _ventasRepository.ActualizarAsync(id, dto, usuario);

    public Task AnularAsync(int id)
        => _ventasRepository.AnularAsync(id);

    public Task SincronizarAsync(DateTime fecha)
        => _ventasRepository.SincronizarAsync(fecha);

    public Task<IEnumerable<ResumenDiario>> ObtenerResumenAsync(DateTime fecha)
        => _ventasRepository.ObtenerResumenAsync(fecha);

    public Task<IEnumerable<MedioPago>> ListarMediosPagoAsync()
        => _parametrosRepository.ListarMediosPagoAsync();

    public Task RecalcularTotalesAsync()
        => _ventasRepository.RecalcularTotalesAsync();
}
