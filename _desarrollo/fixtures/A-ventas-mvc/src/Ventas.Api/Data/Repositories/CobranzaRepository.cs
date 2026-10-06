using Dapper;
using Ventas.Api.Data.Interfaces;
using Ventas.Api.Models;

namespace Ventas.Api.Data.Repositories;

/// <summary>
/// Saldos y pagos de clientes (cuenta corriente).
/// </summary>
public class CobranzaRepository : BaseRepository, ICobranzaRepository
{
    public CobranzaRepository(IConfiguration configuration) : base(configuration)
    {
    }

    public async Task<decimal> ObtenerSaldoAsync(int idCliente)
    {
        const string sql = @"
            /* Migrado de PCK_CLIENTES.FN_SALDO_CLIENTE */
            SELECT NVL(SUM(d.MONTO), 0) - PCK_COBRANZA.FN_TOTAL_PAGADO(:idCliente) AS SALDO
              FROM DOCUMENTOS d
             WHERE d.ID_CLIENTE = :idCliente
               AND d.ESTADO = 'VIGENTE'";

        using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<decimal>(sql, new { idCliente });
    }

    public async Task RegistrarPagoAsync(int idCliente, PagoDto pago)
    {
        // El paquete fue creado con identificadores entre comillas dobles
        const string sql = """
            BEGIN
                "PCK_COBRANZA"."SP_REGISTRAR_PAGO"(:pIdCliente, :pMonto, :pMedioPago);
            END;
            """;

        using var conn = CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            pIdCliente = idCliente,
            pMonto = pago.Monto,
            pMedioPago = pago.MedioPago
        });
    }
}
