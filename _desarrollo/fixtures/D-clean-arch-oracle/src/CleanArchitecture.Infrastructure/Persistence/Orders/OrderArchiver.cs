using System.Data;
using Dapper;

namespace CleanArchitecture.Infrastructure.Persistence.Orders;

/// <summary>Mueve los pedidos antiguos a las tablas históricas.</summary>
/// <remarks>
/// Pensado para un job nocturno que todavía no existe: la clase no está
/// registrada en el contenedor y ningún endpoint la usa.
/// </remarks>
internal sealed class OrderArchiver(DbSession session)
{
    /// <summary>Archiva los pedidos cerrados antes de la fecha indicada.</summary>
    /// <returns>Cantidad de pedidos archivados.</returns>
    public async Task<int> ArchivarAsync(DateTimeOffset hasta, CancellationToken cancellationToken = default)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);

        var parameters = new DynamicParameters();
        parameters.Add("p_hasta", hasta.UtcDateTime);
        parameters.Add("p_archivados", dbType: DbType.Int32, direction: ParameterDirection.Output);

        await connection.ExecuteAsync(new CommandDefinition(
            "BEGIN PCK_PEDIDOS.SP_ARCHIVAR_PEDIDOS(:p_hasta, :p_archivados); END;",
            parameters,
            session.Transaction,
            cancellationToken: cancellationToken));

        return parameters.Get<int>("p_archivados");
    }
}
