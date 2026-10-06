using Microsoft.AspNetCore.Mvc;

namespace Ventas.Api.Controllers;

/// <summary>
/// Controlador base de la API de ventas.
/// Los controladores derivados heredan la ruta "api/[controller]".
/// </summary>
[ApiController]
[Route("api/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    // Devuelve 404 si no hay datos; el front espera { "data": ... } solo cuando es 200
    protected IActionResult OkOrNotFound(object? resultado)
        => resultado is null ? NotFound() : Ok(resultado);
}
