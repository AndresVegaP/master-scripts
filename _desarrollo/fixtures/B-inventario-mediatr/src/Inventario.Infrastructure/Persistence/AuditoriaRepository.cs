using System.Data;
using Dapper;
using Inventario.Application.Abstractions;

namespace Inventario.Infrastructure.Persistence;

public sealed class AuditoriaRepository : IAuditoriaRepository
{
    private readonly IDbConnection _conexion;
    private readonly IDbTransaction? _transaccion;

    public AuditoriaRepository(IDbConnection conexion, IDbTransaction? transaccion)
    {
        _conexion = conexion;
        _transaccion = transaccion;
    }

    public Task RegistrarAsync(string tipo, string detalle)
    {
        // El tercer parametro es el codigo de origen que esperan los reportes legados
        const string bloque = """
            BEGIN
              INVENTARIO.PCK_AUDITORIA.SP_REGISTRAR_EVENTO(:tipo, :detalle, 'PCK_LEGADO.SP_AJUSTE_MANUAL');
            END;
            """;
        return _conexion.ExecuteAsync(bloque, new { tipo, detalle }, _transaccion);
    }
}
