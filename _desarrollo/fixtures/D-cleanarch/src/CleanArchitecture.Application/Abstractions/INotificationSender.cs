namespace CleanArchitecture.Application.Abstractions;

/// <summary>Puerto para avisar a alguien de fuera (correo, SMS, webhook...).</summary>
public interface INotificationSender
{
    // 📘 docs/11-eventos-de-dominio.md
    //
    // Es el segundo puerto del repo, después de los repositorios, y sirve para
    // ver que la inversión de dependencias no es solo cosa de bases de datos:
    // Application dice QUÉ necesita ("avisa de esto") y en Infrastructure vive
    // el CÓMO. Hoy el adaptador solo escribe en el log; mañana puede mandar un
    // correo sin que cambie ni una línea de los casos de uso.

    /// <summary>Envía un aviso.</summary>
    /// <param name="subject">Asunto corto.</param>
    /// <param name="message">Contenido del aviso.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task SendAsync(string subject, string message, CancellationToken cancellationToken = default);
}
