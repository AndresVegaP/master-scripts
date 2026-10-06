using System.Globalization;
using CleanArchitecture.Application.Abstractions;
using CleanArchitecture.Application.Products;
using Dapper;

namespace CleanArchitecture.Infrastructure.Persistence.Products;

/// <summary>Adaptador de lectura: SQL directo a DTO, sin pasar por el agregado.</summary>
internal sealed class ProductQueries(DbSession session) : IProductQueries
{
    // 📘 docs/07-casos-de-uso.md (CQRS)
    //
    // Compara este archivo con ProductRepository: allí se reconstruye el
    // agregado (con sus value objects y sus reglas) porque se va a MODIFICAR;
    // aquí se arma directamente lo que la pantalla necesita. Menos trabajo,
    // menos acoplamiento y consultas que pueden optimizarse a su aire.

    public async Task<PagedResult<ProductDto>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);

        // Migrado de PCK_PRODUCTOS.SP_LISTAR_PRODUCTOS
        // El procedimiento devolvía un REF CURSOR con la página y un OUT con el
        // total; aquí es UNA sola consulta: COUNT(*) OVER () repite el total en
        // cada fila. Oracle 10g no tiene OFFSET/FETCH, así que se pagina con
        // ROWNUM en dos niveles.
        //
        // ORDER BY con DESEMPATE (CreatedAt, Id): si dos productos se crean en
        // el mismo instante, sin el segundo criterio el orden entre ellos no
        // está garantizado y podrían repetirse o perderse entre páginas.
        //
        // Los productos que cargó el proceso legado de importación no salen en
        // el catálogo (la columna Origen guarda qué proceso creó la fila).
        var rows = (await connection.QueryAsync<ProductListRow>(new CommandDefinition(
            """
            SELECT *
            FROM (
                SELECT q.*, ROWNUM AS Rn
                FROM (
                    SELECT p.Id, p.Sku, p.Name, p.PriceAmount, p.PriceCurrency, p.Stock, p.CreatedAt, p.UpdatedAt,
                           PCK_UTIL.FN_FORMATEAR_PRECIO(p.PriceAmount, p.PriceCurrency) AS PriceText,
                           COUNT(*) OVER () AS TotalCount
                    FROM Products p
                    WHERE NVL(p.Origen, 'MANUAL') <> 'PCK_LEGADO.SP_IMPORTAR_CATALOGO'
                    ORDER BY p.CreatedAt, p.Id
                ) q
                WHERE ROWNUM <= :MaxRow
            )
            WHERE Rn > :MinRow
            """,
            new { MaxRow = page * pageSize, MinRow = (page - 1) * pageSize },
            session.Transaction,
            cancellationToken: cancellationToken))).AsList();

        var totalCount = rows.Count > 0 ? rows[0].TotalCount : 0;

        return new PagedResult<ProductDto>(
            [.. rows.Select(row => row.ToDto())],
            page,
            pageSize,
            (int)totalCount);
    }

    private sealed class ProductListRow
    {
        public string Id { get; init; } = string.Empty;

        public string Sku { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string PriceAmount { get; init; } = string.Empty;

        public string PriceCurrency { get; init; } = string.Empty;

        public long Stock { get; init; }

        public string CreatedAt { get; init; } = string.Empty;

        public string? UpdatedAt { get; init; }

        // Precio ya formateado por la base. La pantalla nueva del catálogo lo
        // usará; el DTO actual todavía no lo expone.
        public string? PriceText { get; init; }

        public long TotalCount { get; init; }

        public ProductDto ToDto() =>
            new(
                Guid.Parse(Id),
                Sku,
                Name,
                decimal.Parse(PriceAmount, CultureInfo.InvariantCulture),
                PriceCurrency,
                (int)Stock,
                DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture),
                UpdatedAt is null ? null : DateTimeOffset.Parse(UpdatedAt, CultureInfo.InvariantCulture));
    }
}
