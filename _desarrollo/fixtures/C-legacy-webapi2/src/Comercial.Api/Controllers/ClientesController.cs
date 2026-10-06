using System.Net;
using System.Web.Http;
using Comercial.Data.Dal;
using Comercial.Data.Modelos;

namespace Comercial.Api.Controllers
{
    /// <summary>
    /// Endpoints de clientes. Rutas por atributo (Web API 2).
    /// </summary>
    [RoutePrefix("api/clientes")]
    public class ClientesController : BaseApiController
    {
        [HttpGet]
        [Route("{id:int}")]
        public IHttpActionResult Obtener(int id)
        {
            var cliente = ClientesDal.Obtener(id);
            if (cliente == null)
            {
                return NotFound();
            }
            return Ok(cliente);
        }

        [HttpGet]
        [Route("buscar")]
        public IHttpActionResult Buscar(string texto)
        {
            // el filtro llega como "texto" y el DAL lo envuelve en { % }
            return Ok(ClientesDal.Buscar(texto));
        }

        [HttpPost]
        [Route("")]
        public IHttpActionResult Crear([FromBody] Cliente cliente)
        {
            var id = ClientesDal.Crear(cliente);
            return Created("api/clientes/" + id, new { id });
        }

        [HttpPut]
        [Route("{id:int}")]
        public IHttpActionResult Actualizar(int id, [FromBody] Cliente cliente)
        {
            var filas = ClientesDal.Actualizar(id, cliente);
            return filas == 0 ? (IHttpActionResult)NotFound() : Ok();
        }

        [HttpDelete]
        [Route("{id:int}")]
        public IHttpActionResult Eliminar(int id)
        {
            ClientesDal.Eliminar(id);
            RegistrarAcceso("ELIMINAR_CLIENTE", id);
            return StatusCode(HttpStatusCode.NoContent);
        }

        // Sin atributo de verbo: Web API infiere GET por el prefijo "Get" del nombre
        [Route("{id:int}/resumen")]
        public IHttpActionResult GetResumen(int id) => Ok(ClientesDal.ObtenerResumen<ResumenCliente>(id));
    }
}
