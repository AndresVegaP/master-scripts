using Microsoft.AspNetCore.Mvc;
using Ventas.Api.Models;
using Ventas.Api.Services;

namespace Ventas.Api.Controllers;

// Hereda [ApiController] y [Route("api/[controller]")] desde BaseApiController,
// por lo tanto la ruta base es "api/Ventas".
public class VentasController : BaseApiController
{
    private readonly IVentasService _ventasService;
    private readonly ILogger<VentasController> _logger;

    public VentasController(IVentasService ventasService, ILogger<VentasController> logger)
    {
        _ventasService = ventasService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta)
    {
        var ventas = await _ventasService.ListarAsync(desde, hasta);
        return Ok(ventas);
    }

    /// <summary>
    /// Obtiene la cabecera de una venta (usa PCK_VENTAS.SP_OBTENER_VENTA).
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obtener(int id)
    {
        var venta = await _ventasService.ObtenerAsync(id);
        return OkOrNotFound(venta);
    }

    [HttpGet("{id:int}/detalle")]
    public async Task<IActionResult> ObtenerDetalle(int id)
    {
        var venta = await _ventasService.ObtenerConDetalleAsync(id);
        return OkOrNotFound(venta);
    }

    [HttpPost]
    public async Task<IActionResult> Registrar([FromBody] VentaDto dto)
    {
        var idVenta = await _ventasService.RegistrarAsync(dto);
        // Devuelve 201 con la ubicacion del recurso creado
        return CreatedAtAction(nameof(Obtener), new { id = idVenta }, null);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] VentaDto dto)
    {
        var usuario = User.Identity?.Name ?? "anonimo";
        await _ventasService.ActualizarAsync(id, dto, usuario);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Anular(int id)
    {
        try
        {
            await _ventasService.AnularAsync(id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al anular la venta {IdVenta} en PCK_VENTAS.SP_ANULAR_VENTA", id);
            return Problem("No se pudo anular la venta");
        }
    }

    // Sin atributo de verbo: acepta cualquier metodo HTTP (lo invoca el scheduler)
    [Route("sincronizar")]
    public async Task<IActionResult> Sincronizar()
    {
        await _ventasService.SincronizarAsync(DateTime.Today);
        return Ok(new { mensaje = "Sincronizacion {ok}" });
    }

    // Ruta absoluta: "~/" ignora el prefijo del controlador
    [HttpGet("~/api/v2/ventas/resumen")]
    public async Task<IActionResult> Resumen([FromQuery] DateTime? fecha)
    {
        var resumen = await _ventasService.ObtenerResumenAsync(fecha ?? DateTime.Today);
        return Ok(resumen);
    }

    [HttpGet("medios-pago")]
    public async Task<IActionResult> ListarMediosPago()
    {
        var medios = await _ventasService.ListarMediosPagoAsync();
        return Ok(medios);
    }

    // Uso interno del job nocturno: NO se expone como endpoint
    [NonAction]
    public async Task<IActionResult> RecalcularTotales()
    {
        await _ventasService.RecalcularTotalesAsync();
        return Ok();
    }
}
