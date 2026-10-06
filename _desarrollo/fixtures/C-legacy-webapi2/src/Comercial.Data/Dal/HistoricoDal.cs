using System;
using System.Collections.Generic;
using System.Data;
using Dapper;
using Oracle.ManagedDataAccess.Client;
using Comercial.Data.Infraestructura;
using Comercial.Data.Modelos;

namespace Comercial.Data.Dal
{
    // Histórico de ventas: usa PCK_HISTORICO.SP_VENTAS_HIST (por db link) y PCK_HISTORICO.SP_ARCHIVAR.
    public static class HistoricoDal
    {
        private const string Esquema = "VENTAS_OWN";

        public static IEnumerable<VentaHistorica> ConsultarHistorico(int idCliente)
        {
            var lista = new List<VentaHistorica>();
            using (var conn = ConexionFactory.Crear())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = "PCK_HISTORICO.SP_VENTAS_HIST@DBLINK_HIST";
                cmd.Parameters.Add("p_id_cliente", OracleDbType.Int32).Value = idCliente;
                cmd.Parameters.Add("p_cursor", OracleDbType.RefCursor, ParameterDirection.Output);
                conn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new VentaHistorica
                        {
                            IdVenta = dr.GetInt32(0),
                            Fecha = dr.GetDateTime(1),
                            Total = dr.GetDecimal(2)
                        });
                    }
                }
            }
            return lista;
        }

        public static ResumenHistorico TotalesCliente(int idCliente)
        {
            // Ojo: la etiqueta antigua era "{nombre}" con comillas ' y " mezcladas */
            var origen = "http://intranet/reportes//totales"; // la doble barra de la URL no es comentario
            var marcaComentario = "/* no es comentario";
            var comilla = '"';
            var llave = '{';
            var sql = $@"
                SELECT c.ID_CLIENTE,
                       VENTAS_OWN.PCK_VENTAS.FN_TOTAL_CLIENTE(c.ID_CLIENTE) AS TOTAL,
                       '{{' || c.NOMBRE || '}}' AS ETIQUETA,
                       '""' AS OBSERVACION
                  FROM {Esquema}.CLIENTES c
                 WHERE c.ID_CLIENTE = :idCliente";

            using (var conn = ConexionFactory.Crear())
            {
                var resumen = conn.QueryFirstOrDefault<ResumenHistorico>(sql, new { idCliente });
                if (resumen != null)
                {
                    resumen.Origen = origen;
                    resumen.Etiqueta = (resumen.Etiqueta ?? string.Empty).Trim(comilla, llave, '}');
                    resumen.Observacion = (resumen.Observacion ?? string.Empty).Replace(marcaComentario, string.Empty);
                }
                return resumen;
            }
        }

        public static void Archivar(int anio)
        {
            Console.WriteLine("Archivando con PCK_HISTORICO.SP_ARCHIVAR, año " + anio);
            using (var conn = ConexionFactory.Crear())
            {
                conn.Execute(@"BEGIN ""PCK_HISTORICO"".""SP_ARCHIVAR""(:p_anio); END;", new { p_anio = anio });
            }
        }
    }
}
