using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Application.Products;
using CleanArchitecture.Application.Reports;
using CleanArchitecture.Application.Reports.ClientSummary;
using CleanArchitecture.Application.Reports.MonthlyClose;
using CleanArchitecture.Domain.Orders;
using CleanArchitecture.Domain.Products;
using CleanArchitecture.Infrastructure.Notifications;
using CleanArchitecture.Infrastructure.Persistence;
using CleanArchitecture.Infrastructure.Persistence.Orders;
using CleanArchitecture.Infrastructure.Persistence.Products;
using CleanArchitecture.Infrastructure.Persistence.Reports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure;

/// <summary>Registra los adaptadores de esta capa y prepara la base de datos.</summary>
public static class DependencyInjection
{
    /// <summary>Conecta cada puerto con su implementación concreta.</summary>
    /// <param name="services">Colección de servicios de la aplicación.</param>
    /// <param name="configuration">Configuración, de donde sale la cadena de conexión.</param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // La cadena de conexión viene de la configuración. Validarla AL
        // ARRANCAR (fail fast): mejor que la aplicación no arranque con un
        // mensaje claro, a que explote en la primera petición.
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Falta la cadena de conexión 'ConnectionStrings:Database' en la configuración.");

        // Singleton: la fábrica no tiene estado mutable, solo guarda el texto de
        // la cadena de conexión.
        services.AddSingleton<IDbConnectionFactory>(new OracleConnectionFactory(connectionString));

        // Scoped: UNA sesión (conexión y transacción) por petición, compartida
        // por todos los repositorios que participen en ella.
        services.AddScoped<DbSession>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // ⭐ LAS LÍNEAS MÁS IMPORTANTES DE ESTE ARCHIVO: "cuando alguien pida la
        // interfaz (del dominio o de la aplicación), entrégale este adaptador".
        // Los casos de uso piden la interfaz sin saber qué hay detrás; el
        // contenedor resuelve. Cambiar de base de datos = cambiar estas líneas
        // y escribir los adaptadores nuevos.
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProductQueries, ProductQueries>();

        services.AddScoped<IReportQueries, ReportQueries>();
        services.AddScoped<IClientSummaryQueries, ClientSummaryQueries>();
        services.AddScoped<IProcesosBatch, ProcesosBatchRepository>();
        services.Configure<ProcesosBatchOptions>(configuration.GetSection("ProcesosBatch"));

        // Los avisos se encolan en la base de datos. LoggingNotificationSender
        // se queda en el repo para depurar en local (basta cambiar esta línea).
        services.AddScoped<INotificationSender, OracleNotificationSender>();
        services.AddSingleton<DatabaseMigrator>();

        return services;
    }

    /// <summary>
    /// Aplica las migraciones pendientes. La Api llama a esto al arrancar y no
    /// necesita saber que por dentro hay un migrador con SQL.
    /// </summary>
    public static async Task MigrateDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Creamos un ámbito porque estamos fuera de una petición HTTP; así
        // cualquier servicio Scoped que el migrador necesite en el futuro se
        // podrá resolver y liberar correctamente.
        await using var scope = services.CreateAsyncScope();

        var migrator = scope.ServiceProvider.GetRequiredService<DatabaseMigrator>();
        await migrator.MigrateAsync(cancellationToken);
    }
}
