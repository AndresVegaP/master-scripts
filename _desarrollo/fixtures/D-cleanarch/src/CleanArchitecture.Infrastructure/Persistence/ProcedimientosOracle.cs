namespace CleanArchitecture.Infrastructure.Persistence;

/// <summary>
/// Nombres de los procedimientos almacenados que la API todavía invoca
/// directamente (con CommandType.StoredProcedure).
/// </summary>
internal static class ProcedimientosOracle
{
    // Un solo lugar para los nombres: si el DBA renombra un paquete, se cambia
    // aquí y en ningún otro sitio.

    private const string PaqueteProductos = "PCK_PRODUCTOS";

    /// <summary>Baja de producto con limpieza de históricos.</summary>
    public const string EliminarProducto = $"{PaqueteProductos}.SP_ELIMINAR_PRODUCTO";

    /// <summary>Deja un correo en la cola que procesa el job de avisos.</summary>
    public const string EncolarCorreo = "PKG_NOTIFICACIONES.SP_ENCOLAR_CORREO";
}
