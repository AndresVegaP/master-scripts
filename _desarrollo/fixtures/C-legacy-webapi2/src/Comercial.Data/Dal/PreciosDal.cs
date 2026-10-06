using System.Collections.Generic;
using Dapper;
using log4net;
using Comercial.Data.Infraestructura;
using Comercial.Data.Modelos;

namespace Comercial.Data.Dal
{
    // Reemplaza al SP legado PCK_PRECIOS.SP_LISTA_SEGMENTO (consultas por segmento)
    public static class PreciosDal
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(PreciosDal));

        public static PrecioCalculado Calcular(int idProducto)
        {
            Log.Debug("Cálculo de precio en línea (antes PCK_PRECIOS.FN_CALCULAR_PRECIO)");
            using (var conn = ConexionFactory.Crear())
            {
                return conn.QueryFirstOrDefault<PrecioCalculado>(@"
                    /* Migrado de PCK_PRECIOS.FN_CALCULAR_PRECIO */
                    SELECT lp.ID_PRODUCTO,
                           lp.PRECIO_LISTA * (1 - NVL(lp.DESCUENTO, 0)) AS NETO,
                           PCK_IMPUESTOS.FN_IVA(lp.PRECIO_LISTA * (1 - NVL(lp.DESCUENTO, 0))) AS IVA
                      FROM LISTA_PRECIOS lp
                     WHERE lp.ID_PRODUCTO = :idProducto
                       AND lp.VIGENTE = 'S'", new { idProducto });
            }
        }

        public static IEnumerable<PrecioSegmento> PorSegmento(string codigo)
        {
            using (var conn = ConexionFactory.Crear())
            {
                return conn.Query<PrecioSegmento>(
                    "SELECT SEGMENTO, ID_PRODUCTO, PRECIO FROM LISTA_PRECIOS WHERE SEGMENTO = NVL(:codigo, SEGMENTO)",
                    new { codigo });
            }
        }
    }
}
