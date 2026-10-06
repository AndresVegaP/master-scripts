using Dapper;
using Ventas.Api.Data.Interfaces;
using Ventas.Api.Models;

namespace Ventas.Api.Data.Repositories;

/// <summary>
/// Parametros generales de ventas. Reemplaza a PCK_PARAMETROS.SP_LISTAR_MEDIOS_PAGO.
/// </summary>
public class ParametrosRepository : BaseRepository, IParametrosRepository
{
    public ParametrosRepository(IConfiguration configuration) : base(configuration)
    {
    }

    public async Task<IEnumerable<MedioPago>> ListarMediosPagoAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<MedioPago>(
            "SELECT ID_MEDIO_PAGO, DESCRIPCION FROM MEDIOS_PAGO WHERE ACTIVO = 'S' ORDER BY ORDEN");
    }
}
