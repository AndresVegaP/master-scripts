namespace Comercial.Data.Dal
{
    /// <summary>
    /// Nombres de paquetes y procedimientos Oracle usados por la capa de datos.
    /// </summary>
    public static class Paquetes
    {
        public const string Auditoria = "PCK_AUDITORIA";
        public const string Productos = "PCK_PRODUCTOS";

        public const string PRODUCTOS_INSERTAR = "PCK_PRODUCTOS.SP_INSERTAR";

        // ya no se usa desde la API (quedó del módulo de bajas)
        public const string PRODUCTOS_DESCONTINUAR = "PCK_PRODUCTOS.SP_DESCONTINUAR";
    }
}
