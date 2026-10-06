using System.Web;
using System.Web.Http;

namespace Comercial.Api
{
    public class WebApiApplication : HttpApplication
    {
        protected void Application_Start()
        {
            // Configuración de Web API 2 (rutas por atributo + ruta convencional)
            GlobalConfiguration.Configure(WebApiConfig.Register);
        }
    }
}
