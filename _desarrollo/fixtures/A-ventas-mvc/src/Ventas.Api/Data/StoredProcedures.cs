namespace Ventas.Api.Data;

/// <summary>
/// Nombres de procedimientos almacenados de Oracle usados por los repositorios.
/// </summary>
public static class StoredProcedures
{
    // Paquete principal (se usa para armar nombres con interpolacion)
    public const string PaqueteVentas = "PCK_VENTAS";

    public const string ObtenerVenta = "PCK_VENTAS.SP_OBTENER_VENTA";
    public const string RegistrarVenta = "PCK_VENTAS.SP_REGISTRAR_VENTA";

    // Reservado para el cierre de caja (todavia sin uso en la API)
    public const string CerrarCaja = "PCK_VENTAS.SP_CERRAR_CAJA";

    public const string ObtenerProducto = "PCK_PRODUCTOS.SP_OBTENER_PRODUCTO";
}
