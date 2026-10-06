using Carter;
using Inventario.Application.Almacenes;
using MediatR;

namespace Inventario.Api.Endpoints;

/// <summary>
/// Modulo Carter de almacenes. Se registra con app.MapCarter() (sin grupo /api/v1).
/// </summary>
public sealed class AlmacenesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/almacenes").WithTags("Almacenes");

        g.MapGet("/", async (ISender sender) => Results.Ok(await sender.Send(new ListarAlmacenesQuery())));

        g.MapGet("/{codigo:length(3)}", async (string codigo, ISender sender) =>
            await sender.Send(new ObtenerAlmacenQuery(codigo)) is { } almacen ? Results.Ok(almacen) : Results.NotFound());

        g.MapGet("/{codigo:length(3)}/ubicaciones", async (string codigo, ISender sender) =>
            Results.Ok(await sender.Send(new ListarUbicacionesQuery(codigo))));

        g.MapPost("/{codigo:length(3)}/reservas", ReservarStock);

        g.MapPost("/{codigo:length(3)}/cierre", CerrarAlmacen);
    }

    private static async Task<IResult> ReservarStock(string codigo, ReservaRequest req, ISender sender)
    {
        await sender.Send(new ReservarStockCommand(codigo, req.ProductoId, req.Cantidad));
        return Results.Accepted();
    }

    // SP: PCK_ALMACEN.SP_CERRAR_ALMACEN
    // El nombre real del procedimiento se configura por pais (Procesos:SpCierreAlmacen)
    private static async Task<IResult> CerrarAlmacen(string codigo, ISender sender, CancellationToken ct)
    {
        await sender.Send(new CerrarAlmacenCommand(codigo), ct);
        return Results.NoContent();
    }
}

public sealed record ReservaRequest(Guid ProductoId, decimal Cantidad);
