using System.Configuration;
using Oracle.ManagedDataAccess.Client;

namespace Comercial.Data.Infraestructura
{
    /// <summary>
    /// Crea conexiones Oracle (Oracle.ManagedDataAccess) a partir del Web.config.
    /// </summary>
    public static class ConexionFactory
    {
        private static readonly string CadenaConexion =
            ConfigurationManager.ConnectionStrings["Comercial"].ConnectionString;

        public static OracleConnection Crear()
        {
            var conn = new OracleConnection(CadenaConexion);
            // identifica la sesión en V$SESSION (módulo / acción); no ejecuta SQL
            conn.ModuleName = "Comercial.Api";
            conn.ActionName = "API";
            return conn;
        }
    }
}
