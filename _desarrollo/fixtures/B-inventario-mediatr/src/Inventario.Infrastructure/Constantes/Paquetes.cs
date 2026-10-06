namespace Inventario.Infrastructure.Constantes;

/// <summary>
/// Nombres de paquetes Oracle (sin esquema) usados por la capa de datos.
/// </summary>
public static class Paquetes
{
    public const string Inventario = "PCK_INVENTARIO";

    public const string Almacen = "PCK_ALMACEN";
}

/// <summary>
/// Procedimientos que se invocan con CommandType.StoredProcedure.
/// </summary>
public static class Procedimientos
{
    public const string ReservarStock = "PCK_ALMACEN.SP_RESERVAR_STOCK";

    // Se usara cuando exista el endpoint de liberacion de reservas
    public const string LiberarReserva = "PCK_ALMACEN.SP_LIBERAR_RESERVA";
}
