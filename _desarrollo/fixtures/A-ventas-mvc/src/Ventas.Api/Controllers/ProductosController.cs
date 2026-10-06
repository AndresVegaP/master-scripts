using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Ventas.Api.Services;

namespace Ventas.Api.Controllers;

public class ProductosController : BaseApiController
{
    private readonly IProductosService _productosService;

    public ProductosController(IProductosService productosService)
    {
        _productosService = productosService;
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        return Ok(await _productosService.ListarAsync());
    }

    [HttpGet("categorias")]
    public async Task<IActionResult> ListarCategorias()
    {
        return Ok(await _productosService.ListarCategoriasAsync());
    }

    [HttpGet("[action]/{codigo}")]
    [ActionName("PorCodigo")]
    public async Task<IActionResult> ObtenerPorCodigoAsync(string codigo)
    {
        var producto = await _productosService.ObtenerPorCodigoAsync(codigo);
        return OkOrNotFound(producto);
    }

    [HttpGet("buscar")]
    [SwaggerOperation(Summary = "Busca productos por descripcion", Description = "Package: PCK_PRODUCTOS - SP: SP_BUSCAR_PRODUCTOS")]
    public async Task<IActionResult> Buscar([FromQuery] string filtro, [FromQuery] int maximo = 50)
    {
        return Ok(await _productosService.BuscarAsync(filtro, maximo));
    }

    [HttpGet("{id:int}/precio")]
    public async Task<IActionResult> ObtenerPrecio(int id, [FromQuery] string lista = "GENERAL")
    {
        var precio = await _productosService.ObtenerPrecioAsync(id, lista);
        return precio is null ? NotFound() : Ok(new { idProducto = id, lista, precio });
    }
}
