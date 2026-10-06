namespace CleanArchitecture.Application.Abstractions;

/// <summary>
/// Puerto para agrupar varias escrituras en UNA transacción: o se guardan
/// todas, o no se guarda ninguna.
/// </summary>
public interface IUnitOfWork
{
    // 📘 docs/10-transacciones-y-concurrencia.md
    //
    // ¿CUÁNDO HACE FALTA? Solo cuando un caso de uso escribe en MÁS DE UN sitio
    // y esas escrituras deben ser atómicas. Ejemplo real de este repo:
    // confirmar un pedido guarda el pedido con sus líneas Y descuenta el stock
    // de varios productos. Si se guardara el pedido y fallara el descuento,
    // habría stock vendido dos veces.
    //
    // Para un caso de uso que escribe UNA sola sentencia (crear un producto),
    // no hace falta: esa sentencia ya es atómica por sí sola. No agregues
    // ceremonia donde no aporta.
    //
    // CÓMO SE USA:
    //
    //     await unitOfWork.BeginTransactionAsync(ct);
    //     ... operaciones con los repositorios ...
    //     await unitOfWork.CommitAsync(ct);      // si no se llama, no se guarda nada
    //
    // No hay Rollback explícito a propósito: si el caso de uso devuelve un
    // fallo o lanza una excepción, simplemente NO se llama a Commit y la
    // transacción se deshace sola al terminar la petición. Es la opción
    // segura por defecto: olvidarse de confirmar no corrompe datos.

    /// <summary>Abre una transacción para las siguientes operaciones de los repositorios.</summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Confirma la transacción abierta. Sin esta llamada, nada se guarda.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}
