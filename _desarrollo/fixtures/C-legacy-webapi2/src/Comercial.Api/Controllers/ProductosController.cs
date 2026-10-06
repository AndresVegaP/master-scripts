using System.Collections.Generic;
using System.Net;
using System.Web.Http;
using Comercial.Data.Dal;
using Comercial.Data.Modelos;

namespace Comercial.Api.Controllers
{
    // Controlador sin atributos de ruta: usa la ruta convencional "DefaultApi" (api/{controller}/{id})
    // y el verbo se deduce del prefijo del nombre del método.
    public class ProductosController : ApiController
    {
        // GET api/Productos
        public IEnumerable<Producto> GetAll() => ProductosDal.Listar();

        // GET api/Productos/5
        public IHttpActionResult GetById(int id)
        {
            var producto = ProductosDal.Obtener(id);
            if (producto == null)
            {
                return NotFound();
            }
            return Ok(producto);
        }

        // POST api/Productos
        public IHttpActionResult Post([FromBody] Producto producto)
        {
            ProductosDal.Insertar(producto);
            return Ok();
        }

        // PUT api/Productos/5
        public IHttpActionResult Put(int id, [FromBody] Producto producto)
        {
            var filas = ProductosDal.Actualizar(id, producto);
            return filas == 0 ? (IHttpActionResult)NotFound() : Ok();
        }

        // DELETE api/Productos/5
        public IHttpActionResult Delete(int id)
        {
            // TODO: validar "dependencias" { ventas, stock } antes de borrar
            ProductosDal.Eliminar(id);
            return StatusCode(HttpStatusCode.NoContent);
        }

        // Público pero NO es acción (lo usaba el antiguo flujo de carrito)
        [NonAction]
        public bool ValidarStock(int id, int cantidad) => ProductosDal.ValidarStock(id, cantidad);
    }
}
