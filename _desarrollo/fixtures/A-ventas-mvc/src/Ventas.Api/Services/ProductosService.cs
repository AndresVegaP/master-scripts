using Ventas.Api.Data.Interfaces;
using Ventas.Api.Models;

namespace Ventas.Api.Services;

public interface IProductosService
{
    Task<IEnumerable<Producto>> ListarAsync();
    Task<IEnumerable<Categoria>> ListarCategoriasAsync();
    Task<Producto?> ObtenerPorCodigoAsync(string codigo);
    Task<IEnumerable<Producto>> BuscarAsync(string filtro, int maximo);
    Task<decimal?> ObtenerPrecioAsync(int idProducto, string lista);
}

public class ProductosService : IProductosService
{
    private readonly IProductosRepository _productosRepository;
    private readonly ILogger<ProductosService> _logger;

    public ProductosService(IProductosRepository productosRepository, ILogger<ProductosService> logger)
    {
        _productosRepository = productosRepository;
        _logger = logger;
    }

    public Task<IEnumerable<Producto>> ListarAsync() => _productosRepository.ListarAsync();

    public Task<IEnumerable<Categoria>> ListarCategoriasAsync() => _productosRepository.ListarCategoriasAsync();

    public async Task<Producto?> ObtenerPorCodigoAsync(string codigo)
    {
        var producto = await _productosRepository.ObtenerPorCodigoAsync(codigo);
        if (producto is null)
        {
            _logger.LogWarning("PCK_PRODUCTOS.SP_OBTENER_PRODUCTO no devolvio datos para {Codigo}", codigo);
        }
        return producto;
    }

    public Task<IEnumerable<Producto>> BuscarAsync(string filtro, int maximo)
        => _productosRepository.BuscarAsync(filtro, Math.Clamp(maximo, 1, 200));

    public Task<decimal?> ObtenerPrecioAsync(int idProducto, string lista)
        => _productosRepository.ObtenerPrecioAsync(idProducto, lista.ToUpperInvariant());
}
