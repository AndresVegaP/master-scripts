using System.Text.RegularExpressions;
using Dapper;

namespace CleanArchitecture.Infrastructure.Persistence;

/// <summary>Base de los adaptadores de lectura que todavía evalúan funciones de la base.</summary>
internal abstract class OracleQueryBase(DbSession session)
{
    // Solo se aceptan nombres con la forma PAQUETE.FUNCION: nada de texto libre
    // que pueda terminar concatenado en el SQL.
    private static readonly Regex NombreFuncionValido = new(@"^[A-Z0-9_]+\.[A-Z0-9_]+$", RegexOptions.Compiled);

    /// <summary>Sesión de base de datos de la petición.</summary>
    protected DbSession Session { get; } = session;

    /// <summary>
    /// Evalúa una función escalar de un paquete con un único argumento, con un
    /// SELECT sobre DUAL.
    /// </summary>
    /// <param name="funcion">Nombre completo de la función (PAQUETE.FUNCION).</param>
    /// <param name="valor">Argumento de la función.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    protected async Task<T> EjecutarFuncionAsync<T>(string funcion, object valor, CancellationToken cancellationToken)
    {
        if (!NombreFuncionValido.IsMatch(funcion))
        {
            throw new ArgumentException($"Nombre de función no válido: '{funcion}'.", nameof(funcion));
        }

        var connection = await Session.GetConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<T>(new CommandDefinition(
            $"SELECT {funcion}(:valor) FROM DUAL",
            new { valor },
            Session.Transaction,
            cancellationToken: cancellationToken))
            ?? throw new InvalidOperationException($"La función {funcion} devolvió NULL.");
    }
}
