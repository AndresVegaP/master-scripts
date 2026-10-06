using Inventario.Application.Modelos;

namespace Inventario.Application.Abstractions;

/// <summary>
/// Unidad de trabajo: comparte la conexion y la transaccion entre repositorios.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    IProductoRepository Productos { get; }
    IStockRepository Stock { get; }
    IKardexRepository Kardex { get; }
    IAlmacenRepository Almacenes { get; }
    IUbicacionRepository Ubicaciones { get; }
    IAuditoriaRepository Auditoria { get; }

    Task CommitAsync(CancellationToken ct = default);
}

public interface IRepository<T> where T : class
{
    Task<T?> ObtenerPorCodigoAsync(string codigo);
}

public interface IProductoRepository
{
    Task<ProductoDto?> ObtenerPorIdAsync(Guid id);
    Task<IReadOnlyList<ProductoResumenDto>> ListarAsync(FiltroProductos filtro);
    Task<PrecioDto?> ObtenerPrecioAsync(Guid id, DateTime fecha);
    Task<IReadOnlyList<UnidadDto>> ObtenerUnidadesAsync(Guid id);
    Task<string> GenerarSkuAsync(string categoria);
    Task InsertarAsync(Producto producto);
    Task DesactivarAsync(Guid id);
    Task EliminarAsync(Guid id);
    Task<IReadOnlyList<PrecioDto>> ObtenerHistoricoPreciosAsync(Guid id);
}

public interface IStockRepository
{
    Task<decimal> AjustarAsync(Guid productoId, decimal cantidad, string motivo);
    Task<decimal> ObtenerStockMinimoAsync(Guid productoId);
    Task<StockDto?> ConsultarPorSkuAsync(string sku);
    Task RecalcularDisponibleAsync(Guid productoId);
}

public interface IKardexRepository
{
    Task<IReadOnlyList<MovimientoKardexDto>> ListarMovimientosAsync(Guid productoId, int anio);
}

public interface IAlmacenRepository : IRepository<Almacen>
{
    Task<IReadOnlyList<Almacen>> ListarAsync();
    Task ReservarAsync(string codigo, Guid productoId, decimal cantidad);
    Task EjecutarProcesoAsync(string nombreSp, string codigo);
}

public interface IUbicacionRepository : IRepository<Ubicacion>
{
    Task<IReadOnlyList<Ubicacion>> ListarPorAlmacenAsync(string codigoAlmacen);
}

public interface IAuditoriaRepository
{
    Task RegistrarAsync(string tipo, string detalle);
}

public interface ICacheService
{
    Task InvalidarPorPatronAsync(string patron, CancellationToken ct);
}
