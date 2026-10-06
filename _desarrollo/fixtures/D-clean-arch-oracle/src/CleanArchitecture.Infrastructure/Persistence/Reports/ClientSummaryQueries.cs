using CleanArchitecture.Application.Reports.ClientSummary;
using Dapper;

namespace CleanArchitecture.Infrastructure.Persistence.Reports;

/// <summary>
/// Resumen de cliente para la pantalla de cuenta corriente.
/// Reescritura completa en SQL de PCK_CLIENTES.SP_RESUMEN_CLIENTE.
/// </summary>
internal sealed class ClientSummaryQueries(DbSession session) : IClientSummaryQueries
{
    public async Task<ResumenClienteDto?> GetAsync(long clienteId, CancellationToken cancellationToken = default)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);

        // Solo cuentan los pedidos confirmados: los cancelados no suman.
        return await connection.QuerySingleOrDefaultAsync<ResumenClienteDto>(new CommandDefinition(
            """
            SELECT c.Id AS ClienteId,
                   c.Nombre,
                   COUNT(DISTINCT o.Id) AS Pedidos,
                   NVL(SUM(l.UnitPrice * l.Quantity), 0) AS TotalComprado,
                   MAX(o.PlacedAt) AS UltimaCompra
            FROM Clientes c
            LEFT JOIN Orders o ON o.ClienteId = c.Id AND o.Status = 'Placed'
            LEFT JOIN OrderLines l ON l.OrderId = o.Id
            WHERE c.Id = :ClienteId
            GROUP BY c.Id, c.Nombre
            """,
            new { ClienteId = clienteId },
            session.Transaction,
            cancellationToken: cancellationToken));
    }
}
