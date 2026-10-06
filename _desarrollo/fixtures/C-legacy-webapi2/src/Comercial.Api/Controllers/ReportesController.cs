using System.Linq;
using System.Runtime.Caching;
using System.Web.Http;
using Comercial.Data.Dal;

namespace Comercial.Api.Controllers
{
    [RoutePrefix("api/reportes")]
    public class ReportesController : ApiController
    {
        [HttpGet]
        [Route("ventas-mensuales/{anio:int:min(2000)}/{mes:int?}")]
        public IHttpActionResult VentasMensuales(int anio, int? mes = null)
            => Ok(ReportesDal.VentasMensuales(anio, mes));

        [HttpGet]
        [Route("top-clientes")]
        public IHttpActionResult TopClientes(int top = 10) => Ok(ReportesDal.TopClientes(top));

        [HttpGet]
        [Route("historico/{idCliente:int}")]
        public IHttpActionResult Historico(int idCliente)
        {
            // datos de años anteriores (otra base de datos)
            return Ok(HistoricoDal.ConsultarHistorico(idCliente));
        }

        // Sin [HttpGet]: el verbo se infiere del prefijo "Get" del nombre
        [Route("totales/{idCliente:int}")]
        public IHttpActionResult GetTotales(int idCliente) => Ok(HistoricoDal.TotalesCliente(idCliente));

        [HttpPost]
        [Route("archivar/{anio:int}")]
        public IHttpActionResult Archivar(int anio)
        {
            HistoricoDal.Archivar(anio);
            return Ok(new { anio, estado = "archivado" });
        }

        // Sin atributo de verbo: el prefijo "Delete" => DELETE. Solo limpia la caché en memoria.
        [Route("cache")]
        public IHttpActionResult DeleteCache()
        {
            foreach (var clave in MemoryCache.Default.Select(x => x.Key).ToList())
            {
                MemoryCache.Default.Remove(clave);
            }
            return Ok();
        }
    }
}
