using System.Collections.Generic;
using System.Configuration;
using System.Data;
using Dapper;
using Dapper.Oracle;
using Oracle.ManagedDataAccess.Client;
using Comercial.Data.Infraestructura;
using Comercial.Data.Modelos;

namespace Comercial.Data.Repositorios
{
    public class VentasRepositorio : IVentasRepositorio
    {
        public IEnumerable<Venta> ListarPorCliente(int idCliente)
        {
            var sql = RecursosSql.Leer("Comercial.Data.Queries.Ventas.ListarVentasPorCliente.sql");
            using (var conn = ConexionFactory.Crear())
            {
                return conn.Query<Venta>(sql, new { idCliente });
            }
        }

        /// <summary>
        /// Reemplazo de PCK_VENTAS.SP_OBTENER_VENTA: ahora es un SELECT con JOIN.
        /// </summary>
        public Venta Obtener(int idVenta)
        {
            using (var conn = ConexionFactory.Crear())
            {
                return conn.QueryFirstOrDefault<Venta>(@"
                    SELECT v.ID_VENTA,
                           v.FECHA,
                           v.TOTAL,
                           v.SP_ORIGEN,
                           c.NOMBRE AS CLIENTE
                      FROM VENTAS v
                      JOIN CLIENTES c ON c.ID_CLIENTE = v.ID_CLIENTE
                     WHERE v.ID_VENTA = :idVenta", new { idVenta });
            }
        }

        public int Registrar(VentaDto venta)
        {
            using (var conn = ConexionFactory.Crear())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        // 1) cabecera
                        var cab = new OracleDynamicParameters();
                        cab.Add("p_id_cliente", venta.IdCliente);
                        cab.Add("p_total", venta.Total);
                        cab.Add("p_id_venta", dbType: OracleMappingType.Int32, direction: ParameterDirection.Output);
                        conn.Execute("BEGIN PCK_VENTAS.SP_REGISTRAR_CABECERA(:p_id_cliente, :p_total, :p_id_venta); END;", cab, tx);
                        var idVenta = cab.Get<int>("p_id_venta");

                        // 2) detalle, una llamada por línea
                        foreach (var d in venta.Detalle)
                        {
                            conn.Execute("PCK_VENTAS.SP_REGISTRAR_DETALLE",
                                new { p_id_venta = idVenta, p_id_producto = d.IdProducto, p_cantidad = d.Cantidad },
                                tx,
                                commandType: CommandType.StoredProcedure);
                        }

                        // 3) recálculo de totales (procedimiento suelto, sin paquete)
                        conn.Execute("BEGIN PRC_RECALCULAR_TOTALES(:p_id_venta); END;", new { p_id_venta = idVenta }, tx);

                        tx.Commit();
                        return idVenta;
                    }
                    catch (OracleException)
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        public void Anular(int idVenta)
        {
            using (var conn = ConexionFactory.Crear())
            {
                conn.Execute(
                    "UPDATE VENTAS SET ESTADO = 'A', FEC_ANULACION = SYSDATE WHERE ID_VENTA = :idVenta",
                    new { idVenta });
            }
        }

        public int ContarPendientes()
        {
            using (var conn = ConexionFactory.Crear())
            {
                return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM VENTAS WHERE ESTADO = 'P'"); // ex PCK_VENTAS.FN_CONTAR_PENDIENTES
            }
        }

        public void Reprocesar()
        {
            // el nombre del procedimiento se configura por ambiente (Web.config)
            var nombreSp = ConfigurationManager.AppSettings["Ventas.SpReproceso"];
            OracleHelper.EjecutarSp(nombreSp, new { p_lote = 500 });
        }
    }
}
