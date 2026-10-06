using Microsoft.AspNetCore.Mvc;
using Ventas.Api.Models;
using Ventas.Api.Services;

namespace Ventas.Api.Controllers;

public class ReportesController : BaseApiController
{
    private readonly IReportesService _reportesService;

    public ReportesController(IReportesService reportesService)
    {
        _reportesService = reportesService;
    }

    /// <summary>
    /// Genera un reporte en segundo plano.
    /// Ejecuta PCK_REPORTES.SP_GENERAR_REPORTE (el nombre real se configura por ambiente).
    /// </summary>
    [HttpPost("generar")]
    public async Task<IActionResult> Generar([FromBody] SolicitudReporte solicitud)
    {
        var idSolicitud = await _reportesService.GenerarAsync(solicitud);
        return Accepted(new { idSolicitud });
    }

    [HttpGet("ventas-mensuales/{anio:int?}")]
    public async Task<IActionResult> VentasMensuales(int? anio)
    {
        var datos = await _reportesService.VentasMensualesAsync(anio ?? DateTime.Today.Year);
        return Ok(datos);
    }
}
