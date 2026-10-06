using CleanArchitecture.Application.Abstractions;

namespace CleanArchitecture.Infrastructure.Persistence;

/// <summary>Adaptador de <see cref="IUnitOfWork"/> sobre la sesión de base de datos.</summary>
internal sealed class UnitOfWork(DbSession session) : IUnitOfWork
{
    // Fíjate en lo delgado que es este adaptador: la capa Application pide
    // "agrupa esto en una transacción" sin saber que existen conexiones,
    // SQLite ni ADO.NET. Toda la mecánica vive en DbSession.

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        session.BeginTransactionAsync(cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        session.CommitAsync(cancellationToken);
}
