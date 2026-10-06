using System.Collections.Generic;
using System.Data;
using Dapper;
using Dapper.Oracle;
using Comercial.Data.Infraestructura;
using Comercial.Data.Modelos;

namespace Comercial.Data.Dal
{
    // DAL de productos: mezcla SPs heredados del paquete PCK_PRODUCTOS y consultas reescritas.
    // Ojo con los "precios": vienen de { LISTA_PRECIOS } y no de la tabla PRODUCTOS.
    public static class ProductosDal
    {
        public static IEnumerable<Producto> Listar()
        {
            var p = new OracleDynamicParameters();
            p.Add("p_cursor", dbType: OracleMappingType.RefCursor, direction: ParameterDirection.Output);
            using (var conn = ConexionFactory.Crear())
            {
                return conn.Query<Producto>("VENTAS_OWN.PCK_PRODUCTOS.SP_LISTAR", p, commandType: CommandType.StoredProcedure);
            }
        }

        public static Producto Obtener(int idProducto)
        {
            using (var conn = ConexionFactory.Crear())
            {
                return conn.QueryFirstOrDefault<Producto>(@"
                    SELECT p.ID_PRODUCTO,
                           p.NOMBRE,
                           PCK_PRODUCTOS.FN_PRECIO_VIGENTE(p.ID_PRODUCTO) AS PRECIO
                      FROM PRODUCTOS p
                     WHERE p.ID_PRODUCTO = :idProducto", new { idProducto });
            }
        }

        public static int Insertar(Producto producto) =>
            OracleHelper.EjecutarSp(Paquetes.PRODUCTOS_INSERTAR, new { p_nombre = producto.Nombre, p_precio = producto.PrecioBase });

        public static int Actualizar(int idProducto, Producto producto)
        {
            using (var conn = ConexionFactory.Crear())
            {
                return conn.Execute(@"
                    -- Migrado de PCK_PRODUCTOS.SP_ACTUALIZAR
                    MERGE INTO PRODUCTOS d
                    USING (SELECT :idProducto AS ID_PRODUCTO,
                                  PCK_UTIL.FN_NORMALIZAR_TEXTO(:Nombre) AS NOMBRE,
                                  PCK_PRECIOS.FN_CALCULAR_PRECIO(:idProducto, :PrecioBase) AS PRECIO
                             FROM DUAL) s
                       ON (d.ID_PRODUCTO = s.ID_PRODUCTO)
                     WHEN MATCHED THEN UPDATE
                          SET d.NOMBRE = s.NOMBRE,
                              d.PRECIO = s.PRECIO",
                    new { idProducto, producto.Nombre, producto.PrecioBase });
            }
        }

        public static int Eliminar(int idProducto)
        {
            using (var conn = ConexionFactory.Crear())
            {
                return conn.Execute("DELETE FROM PRODUCTOS WHERE ID_PRODUCTO = :idProducto", new { idProducto });
            }
        }

        public static bool ValidarStock(int idProducto, int cantidad)
        {
            var p = new OracleDynamicParameters();
            p.Add("p_id_producto", idProducto);
            p.Add("p_cantidad", cantidad);
            p.Add("p_ok", dbType: OracleMappingType.Int32, direction: ParameterDirection.Output);
            using (var conn = ConexionFactory.Crear())
            {
                conn.Execute("PCK_STOCK.SP_VALIDAR", p, commandType: CommandType.StoredProcedure);
                return p.Get<int>("p_ok") == 1;
            }
        }
    }
}
