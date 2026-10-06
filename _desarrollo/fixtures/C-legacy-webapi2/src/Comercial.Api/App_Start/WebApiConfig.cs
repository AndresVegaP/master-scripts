using System.Web.Http;

namespace Comercial.Api
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Rutas por atributo: [RoutePrefix] + [Route] en los controladores nuevos
            config.MapHttpAttributeRoutes();

            // Ruta convencional para los controladores antiguos (ProductosController)
            // Ej.: GET api/Productos, GET api/Productos/5, POST api/Productos
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            // Solo JSON, sin XML
            config.Formatters.Remove(config.Formatters.XmlFormatter);
        }
    }
}
