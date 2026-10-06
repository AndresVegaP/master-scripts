using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Application.Orders.CancelOrder;
using CleanArchitecture.Application.Orders.EventHandlers;
using CleanArchitecture.Application.Orders.GetOrderById;
using CleanArchitecture.Application.Orders.PlaceOrder;
using CleanArchitecture.Application.Products.CreateProduct;
using CleanArchitecture.Application.Products.DeleteProduct;
using CleanArchitecture.Application.Products.GetProductById;
using CleanArchitecture.Application.Products.ListProducts;
using CleanArchitecture.Application.Products.UpdateProductPrice;
using CleanArchitecture.Application.Reports;
using CleanArchitecture.Application.Reports.ClientSummary;
using CleanArchitecture.Application.Reports.MonthlyClose;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CleanArchitecture.Application;

/// <summary>Registra en el contenedor de dependencias lo que aporta esta capa.</summary>
public static class DependencyInjection
{
    // 📘 docs/02-clean-architecture.md
    //
    // PATRÓN: cada capa expone un método AddXxx() que registra SUS servicios.
    // Así la Api (composition root) solo escribe:
    //
    //     builder.Services.AddApplication();
    //     builder.Services.AddInfrastructure(builder.Configuration);
    //
    // y cada capa se hace cargo de su propio cableado. Si agregas un caso de
    // uso, lo registras aquí, no en Program.cs.

    /// <summary>Registra los casos de uso y los servicios de aplicación.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // CICLOS DE VIDA — los tres que debes conocer:
        //   Transient: una instancia nueva cada vez que alguien la pide.
        //   Scoped:    una por petición HTTP, compartida durante toda la petición.
        //   Singleton: una para toda la vida de la aplicación.
        //
        // Los casos de uso son Scoped: viven lo que dura la petición y así
        // pueden recibir dependencias Scoped, como los repositorios, que
        // comparten la conexión y la transacción de esa petición.
        services.AddScoped<CreateProductHandler>();
        services.AddScoped<GetProductByIdHandler>();
        services.AddScoped<ListProductsHandler>();
        services.AddScoped<UpdateProductPriceHandler>();
        services.AddScoped<DeleteProductHandler>();

        services.AddScoped<PlaceOrderHandler>();
        services.AddScoped<GetOrderByIdHandler>();
        services.AddScoped<CancelOrderHandler>();

        services.AddScoped<SalesReportHandler>();
        services.AddScoped<LowStockHandler>();
        services.AddScoped<FormatPriceHandler>();
        services.AddScoped<TopProductsHandler>();
        services.AddScoped<ExchangeRateHandler>();
        services.AddScoped<ListCurrenciesHandler>();
        services.AddScoped<ClientSummaryHandler>();
        services.AddScoped<MonthlyCloseHandler>();

        // Eventos de dominio: el despachador y los manejadores. Se registran
        // como IDomainEventHandler para que el despachador los reciba TODOS
        // (cada uno filtra su propio tipo de evento).
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IDomainEventHandler, NotifyWhenOrderPlaced>();
        services.AddScoped<IDomainEventHandler, NotifyWhenOrderCancelled>();

        // El reloj, como dependencia. TimeProvider es la abstracción de .NET 8+
        // para el tiempo: en producción se usa el reloj del sistema y en los
        // tests, un FakeTimeProvider que se puede congelar y adelantar.
        // TryAddSingleton no pisa el registro si alguien (un test) ya puso otro.
        services.TryAddSingleton(TimeProvider.System);

        return services; // devolver services permite encadenar llamadas
    }
}
