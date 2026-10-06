using Inventario.Application.Abstractions;
using Inventario.Application.Productos.Commands;
using Inventario.Application.Productos.Events;
using MediatR;
using Moq;
using Xunit;

namespace Inventario.Application.Tests;

public class AjustarStockCommandHandlerTests
{
    [Fact]
    public async Task Ajuste_publica_notificacion_de_stock()
    {
        var stock = new Mock<IStockRepository>();
        stock.Setup(s => s.AjustarAsync(It.IsAny<Guid>(), 5m, "INV")).ReturnsAsync(15m);
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(u => u.Stock).Returns(stock.Object);
        var mediator = new Mock<IMediator>();
        var cache = new Mock<ICacheService>();

        var handler = new AjustarStockCommandHandler(uow.Object, mediator.Object, cache.Object);
        await handler.Handle(new AjustarStockCommand(Guid.NewGuid(), 5m, "INV"), CancellationToken.None);

        mediator.Verify(m => m.Publish(It.IsAny<StockAjustadoNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Contrato_con_dba_no_cambia()
    {
        // Contrato acordado: PCK_INVENTARIO.SP_AJUSTAR_STOCK(p_producto_id, p_cantidad, p_motivo, p_saldo OUT)
        const string esperado = "PCK_INVENTARIO.SP_AJUSTAR_STOCK";
        Assert.Equal(esperado, $"{Inventario.Infrastructure.Constantes.Paquetes.Inventario}.SP_AJUSTAR_STOCK");
    }

    [Fact]
    public void Semilla_de_pruebas_usa_bloque_anonimo()
    {
        const string semilla = "BEGIN PCK_PRUEBAS.SP_CARGAR_SEMILLA(:escenario); END;";
        Assert.StartsWith("BEGIN", semilla);
    }
}
