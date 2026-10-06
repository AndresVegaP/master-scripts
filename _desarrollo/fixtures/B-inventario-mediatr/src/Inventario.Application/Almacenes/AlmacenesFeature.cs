using Inventario.Application.Abstractions;
using Inventario.Application.Modelos;
using MediatR;
using Microsoft.Extensions.Options;

namespace Inventario.Application.Almacenes;

/// <summary>
/// Opciones de procesos batch configurables por pais.
/// </summary>
public sealed class ProcesosOptions
{
    /// <summary>Nombre completo (paquete.procedimiento) del cierre de almacen.</summary>
    public string SpCierreAlmacen { get; set; } = string.Empty;
}

public sealed record ListarAlmacenesQuery : IRequest<IReadOnlyList<Almacen>>;

public sealed class ListarAlmacenesQueryHandler : IRequestHandler<ListarAlmacenesQuery, IReadOnlyList<Almacen>>
{
    private readonly IUnitOfWork _uow;

    public ListarAlmacenesQueryHandler(IUnitOfWork uow) => _uow = uow;

    public Task<IReadOnlyList<Almacen>> Handle(ListarAlmacenesQuery request, CancellationToken cancellationToken)
        => _uow.Almacenes.ListarAsync();
}

public sealed record ObtenerAlmacenQuery(string Codigo) : IRequest<Almacen?>;

public sealed class ObtenerAlmacenQueryHandler : IRequestHandler<ObtenerAlmacenQuery, Almacen?>
{
    private readonly IUnitOfWork _uow;

    public ObtenerAlmacenQueryHandler(IUnitOfWork uow) => _uow = uow;

    public Task<Almacen?> Handle(ObtenerAlmacenQuery request, CancellationToken cancellationToken)
        => _uow.Almacenes.ObtenerPorCodigoAsync(request.Codigo.ToUpperInvariant());
}

public sealed record ListarUbicacionesQuery(string CodigoAlmacen) : IRequest<IReadOnlyList<Ubicacion>>;

public sealed class ListarUbicacionesQueryHandler : IRequestHandler<ListarUbicacionesQuery, IReadOnlyList<Ubicacion>>
{
    private readonly IUnitOfWork _uow;

    public ListarUbicacionesQueryHandler(IUnitOfWork uow) => _uow = uow;

    public Task<IReadOnlyList<Ubicacion>> Handle(ListarUbicacionesQuery request, CancellationToken cancellationToken)
        => _uow.Ubicaciones.ListarPorAlmacenAsync(request.CodigoAlmacen.ToUpperInvariant());
}

public sealed record ReservarStockCommand(string CodigoAlmacen, Guid ProductoId, decimal Cantidad) : IRequest;

public sealed class ReservarStockCommandHandler : IRequestHandler<ReservarStockCommand>
{
    private readonly IUnitOfWork _uow;

    public ReservarStockCommandHandler(IUnitOfWork uow) => _uow = uow;

    public async Task Handle(ReservarStockCommand request, CancellationToken cancellationToken)
    {
        await _uow.Almacenes.ReservarAsync(request.CodigoAlmacen, request.ProductoId, request.Cantidad);
        await _uow.CommitAsync(cancellationToken);
    }
}

public sealed record CerrarAlmacenCommand(string CodigoAlmacen) : IRequest;

public sealed class CerrarAlmacenCommandHandler : IRequestHandler<CerrarAlmacenCommand>
{
    private readonly IUnitOfWork _uow;
    private readonly ProcesosOptions _opciones;

    public CerrarAlmacenCommandHandler(IUnitOfWork uow, IOptions<ProcesosOptions> opciones)
    {
        _uow = uow;
        _opciones = opciones.Value;
    }

    public async Task Handle(CerrarAlmacenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_opciones.SpCierreAlmacen))
            throw new InvalidOperationException("No hay procedimiento de cierre configurado (Procesos:SpCierreAlmacen)");

        await _uow.Almacenes.EjecutarProcesoAsync(_opciones.SpCierreAlmacen, request.CodigoAlmacen);
        await _uow.CommitAsync(cancellationToken);
    }
}
