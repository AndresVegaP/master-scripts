using CleanArchitecture.Application.Abstractions;

namespace CleanArchitecture.Application.Products;

/// <summary>Puerto de LECTURA de productos: consultas para pantallas, no para cambiar estado.</summary>
public interface IProductQueries
{
    // 📘 docs/07-casos-de-uso.md — CQRS de verdad, no solo de nombre.
    //
    // CQRS separa los dos lados de una aplicación:
    //
    //   ESCRITURA: IProductRepository (Domain) carga el AGREGADO completo,
    //              aplica reglas y lo guarda. Necesita invariantes.
    //   LECTURA:   este puerto va directo al SQL y arma el DTO de la pantalla.
    //              No necesita invariantes: nadie va a modificar nada.
    //
    // ¿Por qué importa? Reconstruir agregados solo para aplanarlos a un DTO es
    // trabajo tirado a la basura: se crean value objects que se descartan al
    // instante y el listado queda atado a las reglas de creación. Con un puerto
    // de lectura, la consulta paginada es un SELECT con LIMIT y OFFSET.
    //
    // ¿Y por qué vive en Application y no en Domain? Porque devuelve DTOs de la
    // aplicación, no agregados: no es un concepto del dominio. Esta es la razón
    // por la que Infrastructure referencia a Application.
    //
    // Cuándo NO hacer esto: en un dominio pequeño con un solo modelo, mantener
    // ambos lados cuesta más de lo que aporta. Aquí convive con GetProductById,
    // que sí pasa por el agregado, para que compares los dos caminos.

    /// <summary>Devuelve una página del catálogo, ordenada por fecha de creación.</summary>
    /// <param name="page">Número de página, empezando en 1.</param>
    /// <param name="pageSize">Cantidad de elementos por página.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<PagedResult<ProductDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
