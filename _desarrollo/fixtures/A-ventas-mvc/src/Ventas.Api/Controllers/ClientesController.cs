using Microsoft.AspNetCore.Mvc;
using Ventas.Api.Models;
using Ventas.Api.Services;

namespace Ventas.Api.Controllers;

public class ClientesController : BaseApiController
{
    private readonly IClientesService _clientesService;

    public ClientesController(IClientesService clientesService)
    {
        _clientesService = clientesService;
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var clientes = await _clientesService.ListarAsync();
        return Ok(clientes);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obtener(int id)
    {
        var cliente = await _clientesService.ObtenerAsync(id);
        return OkOrNotFound(cliente);
    }

    [HttpGet("{id:int}/saldo")]
    public async Task<IActionResult> ObtenerSaldo(int id)
    {
        var saldo = await _clientesService.ObtenerSaldoAsync(id);
        return Ok(new { idCliente = id, saldo });
    }

    [HttpPost("{id:int}/pagos")]
    public async Task<IActionResult> RegistrarPago(int id, [FromBody] PagoDto pago)
    {
        await _clientesService.RegistrarPagoAsync(id, pago);
        return Accepted();
    }

    // El front antiguo usa GET y el nuevo envia POST con el mismo query string
    [HttpGet("buscar")]
    [HttpPost("buscar")]
    public async Task<IActionResult> Buscar([FromQuery] string texto)
    {
        var clientes = await _clientesService.BuscarAsync(texto);
        return Ok(clientes);
    }

    /// <summary>
    /// Elimina (borrado logico) los clientes sin movimientos.
    /// </summary>
    /// <remarks>Usa PCK_CLIENTES.SP_ELIMINAR_INACTIVOS.</remarks>
    [HttpDelete]
    public async Task<IActionResult> EliminarInactivos([FromQuery] int dias = 365)
    {
        var eliminados = await _clientesService.EliminarInactivosAsync(dias);
        return Ok(new { eliminados });
    }
}
