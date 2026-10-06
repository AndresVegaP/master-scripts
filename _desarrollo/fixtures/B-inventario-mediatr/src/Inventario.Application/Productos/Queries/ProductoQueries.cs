using Inventario.Application.Abstractions;
using Inventario.Application.Modelos;
using MediatR;

namespace Inventario.Application.Productos.Queries;

// ---------------------------------------------------------------
// Obtener producto por Id
// ---------------------------------------------------------------
public sealed record ObtenerProductoQuery(Guid Id) : IRequest<ProductoDto?>;

public sealed class ObtenerProductoQueryHandler : IRequestHandler<ObtenerProductoQuery, ProductoDto?>
{
    private readonly IUnitOfWork _uow;

    public ObtenerProductoQueryHandler(IUnitOfWork uow) => _uow = uow;

    public Task<ProductoDto?> Handle(ObtenerProductoQuery request, CancellationToken cancellationToken)
        => _uow.Productos.ObtenerPorIdAsync(request.Id);
}

// ---------------------------------------------------------------
// Listado paginado
// ---------------------------------------------------------------
public sealed record ListarProductosQuery(FiltroProductos Filtro) : IRequest<IReadOnlyList<ProductoResumenDto>>;

public sealed class ListarProductosQueryHandler : IRequestHandler<ListarProductosQuery, IReadOnlyList<ProductoResumenDto>>
{
    private readonly IUnitOfWork _uow;

    public ListarProductosQueryHandler(IUnitOfWork uow) => _uow = uow;

    public Task<IReadOnlyList<ProductoResumenDto>> Handle(ListarProductosQuery request, CancellationToken cancellationToken)
    {
        // El front envia "*" como comodin; Oracle espera '%'
        var filtro = request.Filtro with { Texto = request.Filtro.Texto?.Replace('*', '%') };
        return _uow.Productos.ListarAsync(filtro);
    }
}

// ---------------------------------------------------------------
// Precio vigente
// ---------------------------------------------------------------
public sealed record ObtenerPrecioQuery(Guid Id, DateTime Fecha) : IRequest<PrecioDto?>;

public sealed class ObtenerPrecioQueryHandler : IRequestHandler<ObtenerPrecioQuery, PrecioDto?>
{
    private readonly IUnitOfWork _uow;

    public ObtenerPrecioQueryHandler(IUnitOfWork uow) => _uow = uow;

    public Task<PrecioDto?> Handle(ObtenerPrecioQuery request, CancellationToken cancellationToken)
        => _uow.Productos.ObtenerPrecioAsync(request.Id, request.Fecha.Date);
}

// ---------------------------------------------------------------
// Unidades de medida equivalentes
// ---------------------------------------------------------------
public sealed record ObtenerUnidadesQuery(Guid Id) : IRequest<IReadOnlyList<UnidadDto>>;

public sealed class ObtenerUnidadesQueryHandler : IRequestHandler<ObtenerUnidadesQuery, IReadOnlyList<UnidadDto>>
{
    private readonly IUnitOfWork _uow;

    public ObtenerUnidadesQueryHandler(IUnitOfWork uow) => _uow = uow;

    public Task<IReadOnlyList<UnidadDto>> Handle(ObtenerUnidadesQuery request, CancellationToken cancellationToken)
        => _uow.Productos.ObtenerUnidadesAsync(request.Id);
}
