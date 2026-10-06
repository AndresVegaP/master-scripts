using System;
using System.IO;
using System.Reflection;

namespace Comercial.Data.Infraestructura
{
    /// <summary>
    /// Lee consultas .sql embebidas en el ensamblado (Build Action = Embedded Resource).
    /// </summary>
    public static class RecursosSql
    {
        private static readonly Assembly Ensamblado = typeof(RecursosSql).Assembly;

        public static string Leer(string nombreRecurso)
        {
            using (var stream = Ensamblado.GetManifestResourceStream(nombreRecurso))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException($"No existe el recurso embebido '{nombreRecurso}'");
                }
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
