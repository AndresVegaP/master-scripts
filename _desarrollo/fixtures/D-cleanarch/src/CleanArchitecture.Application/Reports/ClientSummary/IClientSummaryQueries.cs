namespace CleanArchitecture.Application.Reports.ClientSummary;

/// <summary>Resumen de compras de un cliente.</summary>
/// <param name="ClienteId">Identificador del cliente.</param>
/// <param name="Nombre">Nombre del cliente.</param>
/// <param name="Pedidos">Pedidos confirmados.</param>
/// <param name="TotalComprado">Importe total de esos pedidos.</param>
/// <param name="UltimaCompra">Fecha del último pedido confirmado, si lo hay.</param>
public sealed record ResumenClienteDto(
    long ClienteId,
    string Nombre,
    int Pedidos,
    decimal TotalComprado,
    DateTimeOffset? UltimaCompra);

/// <summary>Puerto de lectura del resumen de cliente.</summary>
public interface IClientSummaryQueries
{
    /// <summary>Devuelve el resumen, o null si el cliente no existe.</summary>
    Task<ResumenClienteDto?> GetAsync(long clienteId, CancellationToken cancellationToken = default);
}
