using System.Collections.Generic;
using System.Linq;
using Dapper;
using Comercial.Data.Infraestructura;
using Comercial.Data.Modelos;

namespace Comercial.Data.Dal
{
    /// <summary>
    /// Reportes comerciales. Las consultas viven en la clase anidada <see cref="Sql"/>.
    /// </summary>
    public static class ReportesDal
    {
        /// <summary>Consultas SQL de reportes (constantes).</summary>
        public static class Sql
        {
            // Migrado de PCK_REPORTES.SP_VENTAS_MENSUALES
            // (el SP devolvía { anio, mes, total } en un REF CURSOR "plano")
            public const string VentasMensuales = @"
                SELECT EXTRACT(YEAR FROM v.FECHA)  AS ANIO,
                       EXTRACT(MONTH FROM v.FECHA) AS MES,
                       PCK_UTIL.FN_NOMBRE_MES(EXTRACT(MONTH FROM v.FECHA)) AS NOMBRE_MES,
                       SUM(v.TOTAL) AS TOTAL
                  FROM VENTAS v
                 WHERE EXTRACT(YEAR FROM v.FECHA) = :anio
                   AND (:mes IS NULL OR EXTRACT(MONTH FROM v.FECHA) = :mes)
                 GROUP BY EXTRACT(YEAR FROM v.FECHA), EXTRACT(MONTH FROM v.FECHA)";

            /// <summary>Reemplaza PCK_REPORTES.SP_TOP_CLIENTES</summary>
            public const string TopClientes = @"
                SELECT * FROM (
                    SELECT c.ID_CLIENTE,
                           c.NOMBRE,
                           SUM(v.TOTAL) AS TOTAL,
                           q'[Nota: l'equipo usaba PCK_FAKE.SP_FAKE(1); ignorar]' AS OBS
                      FROM CLIENTES c
                      JOIN VENTAS v ON v.ID_CLIENTE = c.ID_CLIENTE
                     GROUP BY c.ID_CLIENTE, c.NOMBRE
                     ORDER BY TOTAL DESC)
                 WHERE ROWNUM <= :top";
        }

        private static List<T> Consultar<T>(string sql, object parametros)
        {
            using (var conn = ConexionFactory.Crear())
            {
                return conn.Query<T>(sql, parametros).ToList();
            }
        }

        public static List<VentaMensual> VentasMensuales(int anio, int? mes) =>
            Consultar<VentaMensual>(Sql.VentasMensuales, new { anio, mes });

        public static List<TopCliente> TopClientes(int top) =>
            Consultar<TopCliente>(Sql.TopClientes, new { top });
    }
}
