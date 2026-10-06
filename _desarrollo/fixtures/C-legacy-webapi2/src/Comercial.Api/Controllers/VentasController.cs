using System.ComponentModel;
using System.Web.Http;
using Comercial.Data.Modelos;
using Comercial.Data.Repositorios;

namespace Comercial.Api.Controllers
{
    [RoutePrefix("api/ventas")]
    public class VentasController : ApiController
    {
        private readonly IVentasRepositorio _repositorio;

        // Sin contenedor IoC: constructor por defecto con la implementación concreta
        public VentasController() : this(new VentasRepositorio())
        {
        }

        public VentasController(IVentasRepositorio repositorio)
        {
            _repositorio = repositorio;
        }

        [HttpGet]
        [Route("cliente/{idCliente:int}")]
        public IHttpActionResult ListarPorCliente(int idCliente) => Ok(_repositorio.ListarPorCliente(idCliente));

        [HttpGet]
        [Route("{id:int}")]
        public IHttpActionResult Obtener(int id)
        {
            var venta = _repositorio.Obtener(id);
            return venta == null ? (IHttpActionResult)NotFound() : Ok(venta);
        }

        [HttpGet]
        [Route("pendientes")]
        public IHttpActionResult ContarPendientes() => Ok(new { pendientes = _repositorio.ContarPendientes() });

        [HttpPost]
        [Route("")]
        public IHttpActionResult Registrar([FromBody] VentaDto venta)
        {
            var id = _repositorio.Registrar(venta);
            return Created($"api/ventas/{id}", new { id });
        }

        [HttpPost]
        [Route("{id:int}/anular")]
        [Description("Origen: PCK_VENTAS.SP_ANULAR_VENTA")]
        public IHttpActionResult Anular(int id)
        {
            _repositorio.Anular(id);
            return Ok();
        }

        /// <summary>
        /// Reprocesa las ventas pendientes. Usa PCK_VENTAS.SP_REPROCESAR (nombre configurable por ambiente).
        /// </summary>
        [HttpPost]
        [Route("reprocesar")]
        public IHttpActionResult Reprocesar()
        {
            _repositorio.Reprocesar();
            return Ok();
        }
    }
}
