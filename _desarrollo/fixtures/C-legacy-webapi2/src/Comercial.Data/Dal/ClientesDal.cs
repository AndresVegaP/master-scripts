using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using Dapper.Oracle;
using log4net;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using Comercial.Data.Infraestructura;
using Comercial.Data.Modelos;

namespace Comercial.Data.Dal
{
    /// <summary>
    /// Acceso a datos de clientes. Mezcla OracleCommand "a mano" y Dapper.
    /// </summary>
    public static class ClientesDal
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ClientesDal));

        #region SP: PCK_CLIENTES.SP_OBTENER_CLIENTE
        public static Cliente Obtener(int idCliente)
        {
            using (var conn = ConexionFactory.Crear())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = "PCK_CLIENTES.SP_OBTENER_CLIENTE";
                cmd.BindByName = true;
                cmd.Parameters.Add("p_id_cliente", OracleDbType.Int32).Value = idCliente;
                cmd.Parameters.Add("p_cursor", OracleDbType.RefCursor).Direction = ParameterDirection.Output;
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        Log.WarnFormat("PCK_CLIENTES.SP_OBTENER_CLIENTE no devolvió filas para {0}", idCliente);
                        return null;
                    }
                    return Mapear(reader);
                }
            }
        }

        private static Cliente Mapear(IDataRecord r) => new Cliente
        {
            IdCliente = Convert.ToInt32(r["ID_CLIENTE"]),
            Nombre = r["NOMBRE"] as string,
            Email = r["EMAIL"] as string,
            Rut = r["RUT"] as string
        };
        #endregion

        public static IEnumerable<Cliente> Buscar(string texto)
        {
            // el nombre quedó en minúsculas en el código heredado (no cambiar)
            var p = new OracleDynamicParameters();
            p.Add("p_texto", "%" + texto + "%", OracleMappingType.Varchar2);
            p.Add("p_cursor", dbType: OracleMappingType.RefCursor, direction: ParameterDirection.Output);
            using (var conn = ConexionFactory.Crear())
            {
                return conn.Query<Cliente>(
                    "begin pck_clientes.sp_buscar(:p_texto, :p_cursor); end;",
                    p).ToList();
            }
        }

        public static int Crear(Cliente cliente)
        {
            using (var conn = ConexionFactory.Crear())
            using (var cmd = conn.CreateCommand())
            {
                // identificadores con comillas: así está creado el paquete en producción
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = "BEGIN \"PCK_CLIENTES\".\"SP_INSERTAR\"(:p_nombre, :p_email, :p_id); END;";
                cmd.BindByName = true;
                cmd.Parameters.Add("p_nombre", OracleDbType.Varchar2).Value = cliente.Nombre;
                cmd.Parameters.Add("p_email", OracleDbType.Varchar2).Value = cliente.Email;
                var pId = cmd.Parameters.Add("p_id", OracleDbType.Int32, ParameterDirection.Output);
                conn.Open();
                cmd.ExecuteNonQuery();
                if (pId.Value == null || pId.Value == DBNull.Value)
                {
                    throw new DataException("PCK_CLIENTES.SP_INSERTAR no devolvió p_id");
                }
                return ((OracleDecimal)pId.Value).ToInt32();
            }
        }

        public static int Actualizar(int idCliente, Cliente cliente)
        {
            // Package: PCK_CLIENTES / SP: SP_ACTUALIZAR
            // Reescrito como UPDATE directo (el SP solo hacía el UPDATE + COMMIT)
            using (var conn = ConexionFactory.Crear())
            {
                return conn.Execute(@"
                    UPDATE CLIENTES
                       SET NOMBRE    = :Nombre,
                           EMAIL     = :Email,
                           FEC_MODIF = SYSDATE
                     WHERE ID_CLIENTE = :IdCliente",
                    new { cliente.Nombre, cliente.Email, IdCliente = idCliente });
            }
        }

        #region PCK_CLIENTES.SP_ELIMINAR
        public static void Eliminar(int idCliente)
        {
            using (var conn = ConexionFactory.Crear())
            {
                conn.Execute("PCK_CLIENTES.SP_ELIMINAR",
                    new { p_id_cliente = idCliente },
                    commandType: CommandType.StoredProcedure);
            }
        }
        #endregion

        /// <summary>
        /// Resumen financiero del cliente (dos funciones del paquete, desde DUAL).
        /// </summary>
        public static T ObtenerResumen<T>(int idCliente) where T : class, new()
        {
            using (var conn = ConexionFactory.Crear())
            {
                return conn.QuerySingleOrDefault<T>(@"
                    SELECT PCK_CLIENTES.FN_SALDO_CLIENTE(:id) AS SALDO,
                           PCK_CLIENTES.FN_CATEGORIA(:id)     AS CATEGORIA
                      FROM DUAL", new { id = idCliente });
            }
        }
    }
}
