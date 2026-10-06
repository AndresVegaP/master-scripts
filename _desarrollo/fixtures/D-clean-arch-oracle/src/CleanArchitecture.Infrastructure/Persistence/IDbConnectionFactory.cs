using System.Data.Common;

namespace CleanArchitecture.Infrastructure.Persistence;

/// <summary>Crea conexiones abiertas a la base de datos.</summary>
internal interface IDbConnectionFactory
{
    // ¿Por qué una fábrica y no inyectar la conexión directamente? Porque una
    // conexión es un recurso con estado que hay que abrir y cerrar; centralizar
    // su creación permite cambiar el motor en un solo lugar.
    //
    // Devuelve DbConnection (la clase base de ADO.NET) y no IDbConnection
    // porque DbConnection es la que tiene las APIs asíncronas y IAsyncDisposable.

    /// <summary>Crea y abre una conexión nueva.</summary>
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}
