using CleanArchitecture.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Infrastructure.Notifications;

/// <summary>Adaptador de avisos que, por ahora, solo escribe en el log.</summary>
internal sealed class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    // Este adaptador existe para que veas el patrón completo con algo que no es
    // una base de datos: Application definió el puerto INotificationSender y
    // aquí está el único lugar del sistema que sabe CÓMO se avisa.
    //
    // El día que haya que mandar correos de verdad, se escribe un
    // SmtpNotificationSender, se cambia una línea en DependencyInjection y
    // ningún caso de uso se entera.

    public Task SendAsync(string subject, string message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Notificación enviada. Asunto: {Subject}. Mensaje: {Message}", subject, message);

        return Task.CompletedTask;
    }
}
