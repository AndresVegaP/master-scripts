using Microsoft.AspNetCore.Mvc;

namespace Ventas.Api.Controllers;

// No hereda de BaseApiController: declara su propia ruta
[ApiController]
[Route("api/[controller]")]
public class SaludController : ControllerBase
{
    // Respuesta fija { "estado": "ok" }, sin acceso a base de datos
    [HttpGet("[action]")]
    public IActionResult Ping()
    {
        return Ok(new { estado = "ok", hora = DateTime.UtcNow });
    }

    [HttpGet("version")]
    public IActionResult Version()
    {
        return Ok(new
        {
            version = "1.4.2",
            swagger = "http://intranet/ventas/swagger/*",
            plantillaRuta = "api/{controller}/{id?}",
            comentario = "/* no es un comentario */ // tampoco esto"
        });
    }
}
