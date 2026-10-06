using System.Web.Http;
using Comercial.Data.Dal;
using Comercial.Data.Infraestructura;

namespace Comercial.Api.Controllers
{
    /// <summary>
    /// Clase base de los controladores que registran auditoría.
    /// Es abstracta: no se expone como controlador.
    /// </summary>
    public abstract class BaseApiController : ApiController
    {
        protected void RegistrarAcceso(string accion, int idEntidad)
        {
            // Auditoría centralizada: el nombre del paquete viene de las constantes
            var usuario = User?.Identity?.Name ?? "anonimo";
            OracleHelper.EjecutarSp($"{Paquetes.Auditoria}.SP_REGISTRAR_ACCESO",
                new { p_accion = accion, p_id_entidad = idEntidad, p_usuario = usuario });
        }
    }
}
