using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Infrastructure.Persistence;

namespace CleanArchitecture.Infrastructure.Notifications;

/// <summary>Adaptador de avisos que deja cada aviso en la cola de correo de la base de datos.</summary>
internal sealed class OracleNotificationSender(DbSession session) : INotificationSender
{
    // Mismo puerto que LoggingNotificationSender, otro CÓMO: la cola la vacía
    // un job de la base que ya existía antes de la API. Ningún caso de uso se
    // enteró del cambio: solo cambió una línea en DependencyInjection.

    public async Task SendAsync(string subject, string message, CancellationToken cancellationToken = default) =>
        await session.ExecuteSpAsync(
            ProcedimientosOracle.EncolarCorreo,
            new { p_asunto = subject, p_mensaje = message },
            cancellationToken);
}
