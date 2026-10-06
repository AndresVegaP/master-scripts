using Inventario.Application.Modelos;
using Inventario.Application.Productos.Commands;
using Inventario.Application.Productos.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Inventario.Api.Endpoints;

/// <summary>
/// Endpoints de productos (Minimal API). El prefijo "/api/v1" lo aporta el grupo creado en Program.cs.
/// </summary>
public static class ProductosEndpoints
{
    public static RouteGroupBuilder MapProductosEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/productos").WithTags("Productos");

        g.MapGet("/", ListarProductos);

        // Respuesta: { "id": "...", "sku": "ABC-001", "estado": "A" }
        g.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
            await sender.Send(new ObtenerProductoQuery(id)) is { } producto
                ? Results.Ok(producto)
                : Results.NotFound());

        g.MapGet("/{id:guid}/kardex/{anio:int?}", ObtenerKardex);

        g.MapGet("/{id:guid}/precio", ObtenerPrecio);

        g.MapGet("/{id:guid}/unidades", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ObtenerUnidadesQuery(id), ct)));

        g.MapPost("/", async (CrearProductoRequest req, ISender sender) =>
        {
            var id = await sender.Send(new CrearProductoCommand(req.Descripcion, req.Categoria, req.UnidadCodigo, req.PrecioBase));
            return Results.Created($"/api/v1/productos/{id}", new { id });
        });

        g.MapPost("/{id:guid}/ajustes", async (Guid id, AjustarStockRequest req, ISender sender) =>
        {
            await sender.Send(new AjustarStockCommand(id, req.Cantidad, req.Motivo));
            return Results.NoContent();
        });

        // Baja logica (no borra el registro)
        g.MapDelete("/{id:guid}", async (Guid id, ISender sender) =>
        {
            await sender.Send(new DesactivarProductoCommand(id));
            return Results.NoContent();
        });

        return g;
    }

    // Listado paginado con filtros opcionales (texto y categoria)
    private static async Task<IResult> ListarProductos([AsParameters] FiltroProductos filtro, ISender sender)
        => Results.Ok(await sender.Send(new ListarProductosQuery(filtro)));

    /// <summary>
    /// Devuelve el kardex (movimientos valorizados) del producto para un anio.
    /// Reemplaza al procedimiento PCK_INVENTARIO.SP_KARDEX_PRODUCTO del sistema legado.
    /// </summary>
    private static async Task<IResult> ObtenerKardex(Guid id, int? anio, ISender sender, CancellationToken ct)
        => Results.Ok(await sender.Send(new ObtenerKardexQuery(id, anio ?? DateTime.Today.Year), ct));

    [SwaggerOperation(Summary = "Precio vigente del producto", Description = "SP: PCK_INVENTARIO.SP_OBTENER_PRECIO")]
    private static async Task<IResult> ObtenerPrecio(Guid id, [FromQuery] DateTime? fecha, ISender sender)
        => await sender.Send(new ObtenerPrecioQuery(id, fecha ?? DateTime.Today)) is { } precio
            ? Results.Ok(precio)
            : Results.NotFound();
}

public sealed record CrearProductoRequest(string Descripcion, string Categoria, string UnidadCodigo, decimal PrecioBase);

public sealed record AjustarStockRequest(decimal Cantidad, string Motivo);
