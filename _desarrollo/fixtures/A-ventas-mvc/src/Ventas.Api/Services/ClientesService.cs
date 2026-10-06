using Ventas.Api.Data.Interfaces;
using Ventas.Api.Models;

namespace Ventas.Api.Services;

public interface IClientesService
{
    Task<IEnumerable<Cliente>> ListarAsync();
    Task<Cliente?> ObtenerAsync(int id);
    Task<decimal> ObtenerSaldoAsync(int idCliente);
    Task RegistrarPagoAsync(int idCliente, PagoDto pago);
    Task<IEnumerable<Cliente>> BuscarAsync(string texto);
    Task<int> EliminarInactivosAsync(int dias);
}

public class ClientesService : IClientesService
{
    private readonly IClientesRepository _clientesRepository;
    private readonly ICobranzaRepository _cobranzaRepository;
    private readonly ILogger<ClientesService> _logger;

    public ClientesService(
        IClientesRepository clientesRepository,
        ICobranzaRepository cobranzaRepository,
        ILogger<ClientesService> logger)
    {
        _clientesRepository = clientesRepository;
        _cobranzaRepository = cobranzaRepository;
        _logger = logger;
    }

    public Task<IEnumerable<Cliente>> ListarAsync() => _clientesRepository.ListarAsync();

    public Task<Cliente?> ObtenerAsync(int id) => _clientesRepository.ObtenerAsync(id);

    public Task<decimal> ObtenerSaldoAsync(int idCliente) => _cobranzaRepository.ObtenerSaldoAsync(idCliente);

    public async Task RegistrarPagoAsync(int idCliente, PagoDto pago)
    {
        if (pago.Monto <= 0)
        {
            throw new ArgumentException("El monto del pago debe ser mayor a cero", nameof(pago));
        }
        await _cobranzaRepository.RegistrarPagoAsync(idCliente, pago);
    }

    public Task<IEnumerable<Cliente>> BuscarAsync(string texto)
    {
        var criterio = (texto ?? string.Empty).Trim();
        return _clientesRepository.BuscarAsync(criterio);
    }

    public async Task<int> EliminarInactivosAsync(int dias)
    {
        _logger.LogInformation("Ejecutando PCK_CLIENTES.SP_ELIMINAR_INACTIVOS con {Dias} dias", dias);
        return await _clientesRepository.EliminarInactivosAsync(dias);
    }
}
