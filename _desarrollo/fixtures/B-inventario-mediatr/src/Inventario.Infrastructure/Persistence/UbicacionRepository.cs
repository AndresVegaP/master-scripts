using System.Data;
using Inventario.Application.Abstractions;
using Inventario.Application.Modelos;

namespace Inventario.Infrastructure.Persistence;

/// <summary>
/// Ubicaciones fisicas (pasillo / nivel) de cada almacen.
/// Sustituye a PCK_ALMACEN.SP_UBICACIONES_POR_ALMACEN del sistema legado.
/// </summary>
public sealed class UbicacionRepository : Repository<Ubicacion>, IUbicacionRepository
{
    public UbicacionRepository(IDbConnection conexion, IDbTransaction? transaccion)
        : base(conexion, transaccion)
    {
    }

    protected override string NombreTabla => "UBICACIONES";

    public async Task<IReadOnlyList<Ubicacion>> ListarPorAlmacenAsync(string codigoAlmacen)
    {
        var filas = await QueryAsync<Ubicacion>("""
            SELECT CODIGO AS Codigo, ALMACEN_CODIGO AS AlmacenCodigo, PASILLO AS Pasillo, NIVEL AS Nivel
              FROM UBICACIONES
             WHERE ALMACEN_CODIGO = :codigoAlmacen
               AND ACTIVO = 'S'
             ORDER BY PASILLO, NIVEL
            """, new { codigoAlmacen });
        return filas.ToList();
    }
}
