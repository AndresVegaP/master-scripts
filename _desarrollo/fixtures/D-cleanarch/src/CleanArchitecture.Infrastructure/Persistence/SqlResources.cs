using System.Collections.Concurrent;

namespace CleanArchitecture.Infrastructure.Persistence;

/// <summary>Lee los archivos .sql incrustados en el ensamblado (carpeta Queries).</summary>
internal static class SqlResources
{
    // Los .sql largos se guardan como recursos incrustados (ver el .csproj):
    // el DBA los revisa con sus herramientas y el código solo los carga.
    // Se leen una vez y quedan en memoria.

    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>
    /// Devuelve el texto de un recurso por su nombre relativo al ensamblado,
    /// con puntos en lugar de barras (por ejemplo "Queries.Area.Consulta.sql").
    /// </summary>
    public static string Load(string relativeName) =>
        Cache.GetOrAdd(relativeName, static name =>
        {
            var assembly = typeof(SqlResources).Assembly;
            var fullName = $"{assembly.GetName().Name}.{name}";

            using var stream = assembly.GetManifestResourceStream(fullName)
                ?? throw new InvalidOperationException($"No existe el recurso SQL '{fullName}'.");
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        });
}
