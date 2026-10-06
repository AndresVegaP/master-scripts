using System.Data;
using Dapper;

namespace Comercial.Data.Infraestructura
{
    /// <summary>
    /// Utilidades genéricas para ejecutar procedimientos y consultas simples.
    /// </summary>
    public static class OracleHelper
    {
        /// <summary>
        /// Ejecuta un procedimiento almacenado por nombre ("PAQUETE.PROCEDIMIENTO").
        /// </summary>
        public static int EjecutarSp(string nombreSp, object parametros)
        {
            using (var conn = ConexionFactory.Crear())
            {
                return conn.Execute(nombreSp, parametros, commandType: CommandType.StoredProcedure);
            }
        }

        /// <summary>Verifica la conexión con un eco simple.</summary>
        public static string Ping()
        {
            using (var conn = ConexionFactory.Crear())
            {
                return conn.ExecuteScalar<string>("SELECT 'PCK_SALUD.SP_PING() ok' AS ECO FROM DUAL");
            }
        }
    }
}
