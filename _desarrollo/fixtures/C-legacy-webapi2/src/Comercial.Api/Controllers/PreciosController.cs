using System.Web.Http;
using Comercial.Data.Dal;

namespace Comercial.Api.Controllers
{
    [RoutePrefix("api/precios")]
    public class PreciosController : ApiController
    {
        [HttpGet]
        [Route("{idProducto:int}")]
        public IHttpActionResult Calcular(int idProducto) => Ok(PreciosDal.Calcular(idProducto));

        [HttpGet]
        [Route("segmento/{codigo?}")]
        public IHttpActionResult PorSegmento(string codigo = null) => Ok(PreciosDal.PorSegmento(codigo));
    }
}
