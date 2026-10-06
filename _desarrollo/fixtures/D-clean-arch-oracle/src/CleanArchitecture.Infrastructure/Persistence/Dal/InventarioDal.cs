using System.Data.Common;
using Dapper;

namespace CleanArchitecture.Infrastructure.Persistence.Dal;

/// <summary>Accesos sueltos a procedimientos de inventario que siguen en la base de datos.</summary>
internal static class InventarioDal
{
    // Clase estática heredada del primer DAL del proyecto ("a la antigua"). Se
    // mantiene mientras el cálculo del stock consolidado no se reescriba en C#.

    /// <summary>Recalcula el stock consolidado de los productos de un pedido.</summary>
    public static Task RecalcularStockAsync(
        DbConnection connection,
        DbTransaction? transaction,
        Guid orderId,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            "CALL PRC_RECALCULAR_STOCK(:OrderId)",
            new { OrderId = orderId.ToString() },
            transaction,
            cancellationToken: cancellationToken));

    /// <summary>Reconstruye los índices de las tablas de stock. Lo usaba el job semanal.</summary>
    public static Task ReconstruirIndicesAsync(DbConnection connection, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            "CALL PRC_RECONSTRUIR_INDICES()",
            cancellationToken: cancellationToken));
}
