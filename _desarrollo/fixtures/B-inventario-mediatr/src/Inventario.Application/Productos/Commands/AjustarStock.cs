using Inventario.Application.Abstractions;
using Inventario.Application.Productos.Events;
using MediatR;

namespace Inventario.Application.Productos.Commands;

public sealed record AjustarStockCommand(Guid ProductoId, decimal Cantidad, string Motivo) : IRequest;

public sealed class AjustarStockCommandHandler : IRequestHandler<AjustarStockCommand>
{
    private readonly IUnitOfWork _uow;
    private readonly IMediator _mediator;
    private readonly ICacheService _cache;

    public AjustarStockCommandHandler(IUnitOfWork uow, IMediator mediator, ICacheService cache)
    {
        _uow = uow;
        _mediator = mediator;
        _cache = cache;
    }

    public async Task Handle(AjustarStockCommand request, CancellationToken cancellationToken)
    {
        if (request.Cantidad == 0)
            throw new ArgumentException("La cantidad del ajuste no puede ser cero", nameof(request));

        var saldo = await _uow.Stock.AjustarAsync(request.ProductoId, request.Cantidad, request.Motivo);
        await _uow.CommitAsync(cancellationToken);

        await _cache.InvalidarPorPatronAsync("productos/*", cancellationToken);

        // Efectos secundarios (auditoria y alertas) via notificacion MediatR
        await _mediator.Publish(new StockAjustadoNotification(request.ProductoId, request.Cantidad, saldo), cancellationToken);
    }
}
