using Inventario.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Inventario.Application.Productos.Events;

public sealed record StockAjustadoNotification(Guid ProductoId, decimal Cantidad, decimal NuevoSaldo) : INotification;

/// <summary>
/// Deja rastro del ajuste en la tabla de auditoria.
/// </summary>
public sealed class RegistrarAuditoriaStockHandler : INotificationHandler<StockAjustadoNotification>
{
    private readonly IUnitOfWork _uow;

    public RegistrarAuditoriaStockHandler(IUnitOfWork uow) => _uow = uow;

    public async Task Handle(StockAjustadoNotification notification, CancellationToken cancellationToken)
    {
        var detalle = $"{{\"productoId\":\"{notification.ProductoId}\",\"cantidad\":{notification.Cantidad}}}";
        await _uow.Auditoria.RegistrarAsync("AJUSTE_STOCK", detalle);
        await _uow.CommitAsync(cancellationToken);
    }
}

/// <summary>
/// Avisa cuando el saldo queda por debajo del stock minimo.
/// </summary>
public sealed class AlertaStockMinimoHandler : INotificationHandler<StockAjustadoNotification>
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<AlertaStockMinimoHandler> _logger;

    public AlertaStockMinimoHandler(IUnitOfWork uow, ILogger<AlertaStockMinimoHandler> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task Handle(StockAjustadoNotification notification, CancellationToken cancellationToken)
    {
        var minimo = await _uow.Stock.ObtenerStockMinimoAsync(notification.ProductoId);
        if (notification.NuevoSaldo < minimo)
        {
            _logger.LogWarning("Producto {ProductoId} bajo el minimo: {Saldo} < {Minimo}",
                notification.ProductoId, notification.NuevoSaldo, minimo);
        }
    }
}
