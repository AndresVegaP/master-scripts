using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Application.Products;
using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.UnitTests.Fakes;

/// <summary>Unidad de trabajo falsa que solo registra si se confirmó la transacción.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    /// <summary>Cuántas veces se abrió una transacción.</summary>
    public int TransactionsStarted { get; private set; }

    /// <summary>Cuántas veces se confirmó.</summary>
    public int Commits { get; private set; }

    /// <summary>true si se abrió una transacción y NO se confirmó (se deshizo).</summary>
    public bool RolledBack => TransactionsStarted > 0 && Commits == 0;

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        TransactionsStarted++;

        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Commits++;

        return Task.CompletedTask;
    }
}

/// <summary>Despachador que guarda los eventos publicados para poder verificarlos.</summary>
internal sealed class RecordingDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly List<IDomainEvent> _published = [];

    /// <summary>Eventos publicados, en orden.</summary>
    public IReadOnlyList<IDomainEvent> Published => _published;

    public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        _published.AddRange(domainEvents);

        return Task.CompletedTask;
    }
}

/// <summary>Adaptador de avisos que guarda lo enviado en lugar de mandarlo.</summary>
internal sealed class RecordingNotificationSender : INotificationSender
{
    private readonly List<string> _sent = [];

    /// <summary>Asuntos de los avisos enviados.</summary>
    public IReadOnlyList<string> Sent => _sent;

    public Task SendAsync(string subject, string message, CancellationToken cancellationToken = default)
    {
        _sent.Add(subject);

        return Task.CompletedTask;
    }
}

/// <summary>Puerto de lectura falso: devuelve la página que se le configure.</summary>
internal sealed class StubProductQueries(PagedResult<ProductDto> pageToReturn) : IProductQueries
{
    /// <summary>Página solicitada en la última llamada.</summary>
    public int LastPage { get; private set; }

    /// <summary>Tamaño de página solicitado en la última llamada.</summary>
    public int LastPageSize { get; private set; }

    public Task<PagedResult<ProductDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        LastPage = page;
        LastPageSize = pageSize;

        return Task.FromResult(pageToReturn);
    }
}
