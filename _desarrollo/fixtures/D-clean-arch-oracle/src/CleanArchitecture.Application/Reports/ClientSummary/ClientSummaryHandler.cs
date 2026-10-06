namespace CleanArchitecture.Application.Reports.ClientSummary;

/// <summary>Caso de uso: resumen de compras de un cliente.</summary>
public sealed class ClientSummaryHandler(IClientSummaryQueries clientSummaryQueries)
{
    /// <summary>Ejecuta el caso de uso. Null si el cliente no existe.</summary>
    public Task<ResumenClienteDto?> ExecuteAsync(long clienteId, CancellationToken cancellationToken = default) =>
        clientSummaryQueries.GetAsync(clienteId, cancellationToken);
}
