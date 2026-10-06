using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace CleanArchitecture.IntegrationTests.Api;

/// <summary>Levanta la API completa en memoria para los tests de contrato.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // 📘 docs/09-tests.md
    //
    // WebApplicationFactory ejecuta tu Program.cs REAL —inyección de
    // dependencias real, pipeline real, endpoints reales, Dapper real— pero sin
    // abrir un puerto: las peticiones viajan en memoria por el HttpClient que
    // devuelve CreateClient(). Es lo más parecido a producción que se puede
    // probar sin desplegar.
    //
    // Lo ÚNICO que cambiamos es la cadena de conexión: cada instancia usa su
    // propio archivo SQLite temporal, que se borra al terminar.

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"cleanarch-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development, igual que cuando ejecutas la aplicación en tu máquina:
        // así también se prueban /openapi y el manejo de peticiones malformadas
        // tal como los verás al desarrollar.
        builder.UseEnvironment(Environments.Development);

        // UseSetting pisa el valor de appsettings.json antes de arrancar, con la
        // misma mecánica que usarías en producción con una variable de entorno.
        builder.UseSetting("ConnectionStrings:Database", $"Data Source={_databasePath}");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing); // primero se apaga la aplicación

        // LECCIÓN DE POOLING: aunque la aplicación ya esté apagada, ADO.NET
        // mantiene las conexiones "cerradas" vivas en un pool (por eso abrir
        // conexiones es barato). Mientras el pool retenga una, el archivo .db
        // sigue bloqueado en Windows y File.Delete lanza IOException.
        // ClearAllPools suelta esos recursos de verdad.
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
