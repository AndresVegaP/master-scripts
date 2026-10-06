using Ventas.Api.Models;

namespace Ventas.Api.Data.Interfaces;

public interface IVentasRepository
{
    Task<IEnumerable<VentaResumen>> ListarAsync(DateTime? desde, DateTime? hasta);
    Task<Venta?> ObtenerVentaAsync(int id);
    Task<VentaDetalle?> ObtenerVentaAsync(int id, bool incluirDetalle);
    Task<string> ObtenerSiguienteFolioAsync(string serie);
    Task<int> RegistrarAsync(VentaDto dto);
    Task ActualizarAsync(int id, VentaDto dto, string usuario);
    Task AnularAsync(int id);
    Task SincronizarAsync(DateTime fecha);
    Task<IEnumerable<ResumenDiario>> ObtenerResumenAsync(DateTime fecha);
    Task RecalcularTotalesAsync();
}

public interface IClientesRepository
{
    Task<IEnumerable<Cliente>> ListarAsync();
    Task<Cliente?> ObtenerAsync(int id);
    Task<IEnumerable<Cliente>> BuscarAsync(string texto);
    Task<int> EliminarInactivosAsync(int diasInactividad);
}

public interface ICobranzaRepository
{
    Task<decimal> ObtenerSaldoAsync(int idCliente);
    Task RegistrarPagoAsync(int idCliente, PagoDto pago);
}

public interface IProductosRepository
{
    Task<IEnumerable<Producto>> ListarAsync();
    Task<IEnumerable<Categoria>> ListarCategoriasAsync();
    Task<Producto?> ObtenerPorCodigoAsync(string codigo);
    Task<IEnumerable<Producto>> BuscarAsync(string filtro, int maximo);
    Task<decimal?> ObtenerPrecioAsync(int idProducto, string lista);
    Task<int> DescontinuarAsync(int idProducto);
}

public interface IReportesRepository
{
    Task<IEnumerable<VentaMensual>> VentasMensualesAsync(int anio);
    Task<int> GenerarAsync(string tipoReporte, DateTime desde, DateTime hasta);
}

public interface IParametrosRepository
{
    Task<IEnumerable<MedioPago>> ListarMediosPagoAsync();
}
