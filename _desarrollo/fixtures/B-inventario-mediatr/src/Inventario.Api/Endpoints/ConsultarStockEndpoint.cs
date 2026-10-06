using FastEndpoints;
using Inventario.Application.Abstractions;
using Inventario.Application.Modelos;

namespace Inventario.Api.Endpoints;

public sealed class ConsultarStockRequest
{
    public string Sku { get; set; } = string.Empty;
}

/// <summary>
/// Endpoint FastEndpoints: consulta directa al repositorio, sin MediatR.
/// </summary>
public sealed class ConsultarStockEndpoint : Endpoint<ConsultarStockRequest, StockDto>
{
    private readonly IUnitOfWork _uow;

    public ConsultarStockEndpoint(IUnitOfWork uow) => _uow = uow;

    public override void Configure()
    {
        Get("/api/v1/stock/{sku}");
        AllowAnonymous();
        Summary(s => s.Summary = "Consulta stock disponible y reservado por SKU");
    }

    public override async Task HandleAsync(ConsultarStockRequest req, CancellationToken ct)
    {
        var stock = await _uow.Stock.ConsultarPorSkuAsync(req.Sku.Trim().ToUpperInvariant());
        if (stock is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(stock, ct);
    }
}
