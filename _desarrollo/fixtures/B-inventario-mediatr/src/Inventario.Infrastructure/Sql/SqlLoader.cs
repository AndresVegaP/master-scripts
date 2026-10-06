using System.Collections.Concurrent;
using System.Reflection;

namespace Inventario.Infrastructure.Sql;

/// <summary>
/// Carga consultas SQL embebidas como recurso (Sql/**/*.sql) y las cachea en memoria.
/// </summary>
public static class SqlLoader
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();
    private static readonly Assembly Ensamblado = typeof(SqlLoader).Assembly;

    public static string Load(string nombreArchivo) =>
        Cache.GetOrAdd(nombreArchivo, static nombre =>
        {
            var recurso = Ensamblado.GetManifestResourceNames()
                .SingleOrDefault(r => r.EndsWith("." + nombre, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"No existe el recurso embebido {nombre} (patron Sql/*.sql)");

            using var stream = Ensamblado.GetManifestResourceStream(recurso)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
}
