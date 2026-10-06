using System;
using System.Data;
using Dapper;
using Oracle.ManagedDataAccess.Client;
using Comercial.Data.Infraestructura;

namespace Comercial.Data.Dal.Legacy
{
    /// <summary>
    /// DAL antiguo. Ya no se usa desde la API; se mantiene por el batch nocturno.
    /// </summary>
    [Obsolete("Usar ClientesDal")]
    public static class ClientesLegacyDal
    {
        public static DataTable ObtenerAntiguo(int idCliente)
        {
            var tabla = new DataTable("CLIENTE");
            using (var conn = ConexionFactory.Crear())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = "PCK_CLIENTES_OLD.SP_OBTENER";
                cmd.Parameters.Add("p_id", OracleDbType.Int32).Value = idCliente;
                cmd.Parameters.Add("p_cursor", OracleDbType.RefCursor, ParameterDirection.Output);
                using (var da = new OracleDataAdapter(cmd))
                {
                    da.Fill(tabla);
                }
            }
            return tabla;
        }

        public static void DepurarLog() =>
            ConexionFactory.Crear().Execute("BEGIN PKG_MANTENCION.PRC_DEPURAR_LOG; END;");
    }
}
