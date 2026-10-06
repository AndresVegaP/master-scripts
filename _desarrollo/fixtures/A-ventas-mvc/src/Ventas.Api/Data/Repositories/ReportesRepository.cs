using System.Data;
using Dapper;
using Dapper.Oracle;
using Ventas.Api.Data.Interfaces;
using Ventas.Api.Models;

namespace Ventas.Api.Data.Repositories;

public class ReportesRepository : BaseRepository, IReportesRepository
{
    private readonly IConfiguration _configuration;

    // La consulta mensual se mantiene en un archivo .sql que se copia al directorio de salida
    private static readonly string SqlVentasMensuales =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Queries", "Reportes", "VentasMensuales.sql"));

    public ReportesRepository(IConfiguration configuration) : base(configuration)
    {
        _configuration = configuration;
    }

    public async Task<IEnumerable<VentaMensual>> VentasMensualesAsync(int anio)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<VentaMensual>(SqlVentasMensuales, new { anio });
    }

    public async Task<int> GenerarAsync(string tipoReporte, DateTime desde, DateTime hasta)
    {
        // El nombre del procedimiento se define por ambiente (no esta en el codigo)
        var spName = _configuration[$"Reportes:{tipoReporte}:Procedimiento"]
            ?? throw new InvalidOperationException($"No hay procedimiento configurado para '{tipoReporte}'");

        var p = new OracleDynamicParameters();
        p.Add("pDesde", desde, OracleMappingType.Date, ParameterDirection.Input);
        p.Add("pHasta", hasta, OracleMappingType.Date, ParameterDirection.Input);
        p.Add("pIdSolicitud", dbType: OracleMappingType.Int32, direction: ParameterDirection.Output);

        using var conn = CreateConnection();
        await conn.ExecuteAsync(spName, p, commandType: CommandType.StoredProcedure);
        return p.Get<int>("pIdSolicitud");
    }
}
