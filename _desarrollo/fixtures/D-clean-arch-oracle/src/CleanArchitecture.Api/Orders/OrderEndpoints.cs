using CleanArchitecture.Api.ErrorHandling;
using CleanArchitecture.Application.Orders.CancelOrder;
using CleanArchitecture.Application.Orders.GetOrderById;
using CleanArchitecture.Application.Orders.PlaceOrder;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CleanArchitecture.Api.Orders;

/// <summary>Endpoints HTTP del recurso pedidos.</summary>
public static class OrderEndpoints
{
    // 📘 docs/08-api-http-y-openapi.md
    //
    // DOS ESTILOS DE API EN EL MISMO REPO, a propósito:
    //   - Productos usa rutas de RECURSO (PUT /api/products/{id}/price): se
    //     reemplaza la representación de algo y repetir la petición da el mismo
    //     resultado (idempotente).
    //   - Pedidos usa rutas de COMANDO (POST /api/orders/{id}/cancel): "cancelar"
    //     es una acción del negocio con reglas propias, no el reemplazo de un
    //     campo. Modelarla como POST a una acción refleja el lenguaje ubicuo y
    //     deja lugar a otras (devolver, reenviar...).
    // La comparación entre ambos estilos está en docs/08.

    /// <summary>Registra las rutas del recurso pedidos.</summary>
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/orders").WithTags("Pedidos");

        group.MapPost("/", PlaceOrder)
            .WithName(nameof(PlaceOrder))
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}", GetOrderById)
            .WithName(nameof(GetOrderById))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/cancel", CancelOrder)
            .WithName(nameof(CancelOrder))
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>Confirma un pedido y descuenta el stock de los productos.</summary>
    /// <remarks>
    /// El precio unitario se congela con el precio que tiene el producto en este momento.
    /// Si algún producto no tiene stock suficiente, la respuesta indica todos los que faltan.
    /// </remarks>
    /// <response code="201">Pedido confirmado. La cabecera Location apunta a su URL.</response>
    /// <response code="400">Faltan campos obligatorios o la cantidad está fuera de rango.</response>
    /// <response code="404">Alguno de los productos pedidos no existe.</response>
    /// <response code="409">No hay stock suficiente, o algún producto cambió mientras se procesaba el pedido.</response>
    /// <response code="422">Los datos violan una regla de negocio (moneda no admitida, cantidad inválida...).</response>
    public static async Task<Results<CreatedAtRoute<OrderResponse>, ProblemHttpResult>> PlaceOrder(
        PlaceOrderRequest request,
        PlaceOrderHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new PlaceOrderCommand(
            request.Currency!,
            [.. request.Lines!.Select(line => new PlaceOrderLine(line.ProductId!.Value, line.Quantity))]);

        var result = await handler.ExecuteAsync(command, cancellationToken);

        if (result.IsFailure)
        {
            return result.Error.ToProblem(httpContext);
        }

        var response = OrderResponse.FromDto(result.Value);

        return TypedResults.CreatedAtRoute(response, nameof(GetOrderById), new { id = response.Id });
    }

    /// <summary>Obtiene un pedido con sus líneas.</summary>
    /// <remarks>Reemplaza a PCK_PEDIDOS.SP_CONSULTAR_PEDIDO_COMPLETO de Oracle.</remarks>
    /// <param name="id">Identificador del pedido.</param>
    /// <response code="200">El pedido solicitado.</response>
    /// <response code="404">No existe un pedido con ese identificador.</response>
    public static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> GetOrderById(
        Guid id,
        GetOrderByIdHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.ExecuteAsync(new GetOrderByIdQuery(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(OrderResponse.FromDto(result.Value))
            : result.Error.ToProblem(httpContext);
    }

    /// <summary>Cancela un pedido confirmado y devuelve las unidades al stock.</summary>
    /// <param name="id">Identificador del pedido.</param>
    /// <response code="200">Pedido cancelado.</response>
    /// <response code="404">No existe un pedido con ese identificador.</response>
    /// <response code="409">El pedido no está confirmado (ya se canceló o sigue en borrador).</response>
    public static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> CancelOrder(
        Guid id,
        CancelOrderHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.ExecuteAsync(new CancelOrderCommand(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(OrderResponse.FromDto(result.Value))
            : result.Error.ToProblem(httpContext);
    }
}
