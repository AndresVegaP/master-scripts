using System;
using System.Web.Http;
using Comercial.Data.Infraestructura;

namespace Comercial.Api.Controllers
{
    [RoutePrefix("api/salud")]
    public class SaludController : ApiController
    {
        [HttpGet]
        [Route("")]
        public IHttpActionResult Ping() => Ok(new { estado = OracleHelper.Ping(), servidor = Environment.MachineName });
    }
}
