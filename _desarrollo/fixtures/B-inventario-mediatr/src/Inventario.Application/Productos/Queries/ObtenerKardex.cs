using Inventario.Application.Modelos;
using Inventario.Application.Services;
using MediatR;

namespace Inventario.Application.Productos.Queries;

public sealed record ObtenerKardexQuery(Guid ProductoId, int Anio) : IRequest<IReadOnlyList<MovimientoKardexDto>>;

public sealed class ObtenerKardexQueryHandler : IRequestHandler<ObtenerKardexQuery, IReadOnlyList<MovimientoKardexDto>>
{
    private readonly IKardexService _kardex;

    public ObtenerKardexQueryHandler(IKardexService kardex) => _kardex = kardex;

    public Task<IReadOnlyList<MovimientoKardexDto>> Handle(ObtenerKardexQuery request, CancellationToken cancellationToken)
        => _kardex.ObtenerAsync(request.ProductoId, request.Anio, cancellationToken);
}
