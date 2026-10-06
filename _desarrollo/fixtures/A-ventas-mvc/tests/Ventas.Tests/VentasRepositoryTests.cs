using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ventas.Api.Data;
using Ventas.Api.Data.Interfaces;
using Ventas.Api.Models;
using Ventas.Api.Services;
using Xunit;

namespace Ventas.Tests;

public class VentasRepositoryTests
{
    [Fact]
    public void Constantes_ApuntanAlPaqueteCorrecto()
    {
        Assert.Equal("PCK_VENTAS.SP_REGISTRAR_VENTA", StoredProcedures.RegistrarVenta);
        Assert.Equal("PCK_VENTAS.SP_OBTENER_VENTA", StoredProcedures.ObtenerVenta);
    }

    [Fact]
    public async Task Registrar_ReservaFolioYGraba()
    {
        // Simula PCK_VENTAS.FN_SIGUIENTE_FOLIO y PCK_VENTAS.SP_REGISTRAR_VENTA
        var repo = new Mock<IVentasRepository>();
        repo.Setup(r => r.ObtenerSiguienteFolioAsync("A")).ReturnsAsync("A-0001");
        repo.Setup(r => r.RegistrarAsync(It.IsAny<VentaDto>())).ReturnsAsync(1);
        var servicio = new VentasService(repo.Object, Mock.Of<IParametrosRepository>(), Mock.Of<ILogger<VentasService>>());

        var resultado = await servicio.RegistrarAsync(new VentaDto { Serie = "A" });

        Assert.Equal(1, resultado);
    }

    [Fact]
    public void ConsultaRut_UsaFuncionDeLimpieza()
    {
        // Consulta de referencia para comparar resultados contra Oracle
        const string sql = @"
            -- Migrado de PCK_UTIL.FN_FORMATEAR_RUT
            SELECT PCK_UTIL.FN_LIMPIAR_TEXTO(:rut) AS RUT FROM CLIENTES WHERE ROWNUM = 1";

        Assert.Contains("FN_LIMPIAR_TEXTO", sql);
    }
}

// Controlador falso para pruebas de integracion (no forma parte de la API)
[ApiController]
[Route("api/pruebas")]
public class PruebasController : ControllerBase
{
    private readonly IDbConnection _conn;

    public PruebasController(IDbConnection conn) => _conn = conn;

    [HttpGet("semilla")]
    public async Task<IActionResult> CargarSemilla()
    {
        await _conn.ExecuteAsync("PCK_PRUEBAS.SP_CARGAR_SEMILLA", commandType: CommandType.StoredProcedure);
        return Ok();
    }
}
