using System.Data.Common;
using Oracle.ManagedDataAccess.Client;

namespace CleanArchitecture.Infrastructure.Persistence;

/// <summary>Fábrica de conexiones para Oracle (ODP.NET Managed).</summary>
internal sealed class OracleConnectionFactory(string connectionString) : IDbConnectionFactory
{
    // 📘 docs/06-repositorios-dapper-y-snapshots.md
    //
    // Pasar de SQLite a Oracle significó reescribir SOLO esta capa: esta
    // fábrica, los repositorios y las consultas. Domain, Application y Api no
    // se enteraron: esa es la ganancia real de la arquitectura, y es mucha.

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new OracleConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);

            // Identifica la sesión en V$SESSION: el DBA ve de dónde viene cada
            // conexión sin tener que preguntar.
            connection.ModuleName = "CleanArchitecture.Api";

            return connection;
        }
        catch
        {
            // Si algo falla después de crear la conexión, hay que liberarla o
            // se queda ocupando un hueco del pool.
            await connection.DisposeAsync();
            throw;
        }
    }
}
