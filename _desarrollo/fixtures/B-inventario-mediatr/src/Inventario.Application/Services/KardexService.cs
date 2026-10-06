using Inventario.Application.Abstractions;
using Inventario.Application.Modelos;

namespace Inventario.Application.Services;

public interface IKardexService
{
    Task<IReadOnlyList<MovimientoKardexDto>> ObtenerAsync(Guid productoId, int anio, CancellationToken ct);
}

public sealed class KardexService : IKardexService
{
    private readonly IUnitOfWork _uow;

    public KardexService(IUnitOfWork uow) => _uow = uow;

    public async Task<IReadOnlyList<MovimientoKardexDto>> ObtenerAsync(Guid productoId, int anio, CancellationToken ct)
    {
        if (anio < 2000 || anio > DateTime.Today.Year)
            throw new ArgumentOutOfRangeException(nameof(anio), anio, "Anio fuera de rango");

        var movimientos = await _uow.Kardex.ListarMovimientosAsync(productoId, anio);

        // El saldo acumulado se calcula en memoria (antes lo calculaba la base de datos)
        decimal saldo = 0;
        return movimientos
            .OrderBy(m => m.Fecha)
            .Select(m => m with { Saldo = saldo += m.Cantidad })
            .ToList();
    }
}
