using System.ComponentModel.DataAnnotations;
using CleanArchitecture.Api.Common;
using CleanArchitecture.Api.ErrorHandling;
using CleanArchitecture.Application.Products.CreateProduct;
using CleanArchitecture.Application.Products.DeleteProduct;
using CleanArchitecture.Application.Products.GetProductById;
using CleanArchitecture.Application.Products.ListProducts;
using CleanArchitecture.Application.Products.UpdateProductPrice;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CleanArchitecture.Api.Products;

/// <summary>Endpoints HTTP del recurso productos.</summary>
public static class ProductEndpoints
{
    // 📘 docs/08-api-http-y-openapi.md
    //
    // EL PAPEL DE UN ENDPOINT EN CLEAN ARCHITECTURE: ser un adaptador fino.
    // Cada uno hace exactamente tres cosas, siempre las mismas:
    //   1. TRADUCIR la petición HTTP a un comando o query de Application.
    //   2. INVOCAR el caso de uso.
    //   3. TRADUCIR el Result a una respuesta HTTP.
    //
    // CERO lógica de negocio. Gracias a eso, la API es "una piel": mañana puedes
    // agregar una app de consola o un consumidor de mensajes que invoque LOS
    // MISMOS casos de uso.
    //
    // Fíjate en el TIPO DE RETORNO de cada método: Results<Ok<T>, ProblemHttpResult>
    // dice, en la firma, qué respuestas puede producir. El compilador te avisa si
    // devuelves algo no declarado, y .NET lo usa para generar el documento
    // OpenAPI. Los métodos son públicos y estáticos a propósito: así .NET puede
    // leer sus comentarios /// y convertirlos en la documentación de la API.

    /// <summary>Registra las rutas del recurso productos.</summary>
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // MapGroup: prefijo y metadatos comunes una sola vez.
        var group = app.MapGroup("/api/products").WithTags("Productos");

        group.MapGet("/", ListProducts)
            .WithName(nameof(ListProducts))
            .ProducesValidationProblem();

        // "{id:guid}" es una RESTRICCIÓN de ruta: si el valor no es un Guid,
        // ASP.NET Core responde 404 sin llegar a ejecutar nuestro código.
        group.MapGet("/{id:guid}", GetProductById)
            .WithName(nameof(GetProductById))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateProduct)
            .WithName(nameof(CreateProduct))
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // La ruta expresa la ACCIÓN de negocio sobre un sub-recurso (el precio),
        // no un "update genérico" del producto. PUT porque es idempotente:
        // repetir la misma petición deja el mismo estado.
        group.MapPut("/{id:guid}/price", UpdateProductPrice)
            .WithName(nameof(UpdateProductPrice))
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{id:guid}", DeleteProduct)
            .WithName(nameof(DeleteProduct))
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>Lista el catálogo de productos.</summary>
    /// <remarks>Devuelve una página ordenada por fecha de creación. Ambos parámetros son opcionales.</remarks>
    /// <param name="page">Número de página, empezando en 1.</param>
    /// <param name="pageSize">Cantidad de elementos por página (máximo 100).</param>
    /// <response code="200">Página del catálogo.</response>
    /// <response code="400">Los parámetros de paginación no son válidos.</response>
    public static async Task<Results<Ok<PagedResponse<ProductResponse>>, ProblemHttpResult>> ListProducts(
        ListProductsHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        // Los parámetros de consulta se declaran con valor por defecto para que
        // sean OPCIONALES: "GET /api/products" a secas tiene que funcionar.
        // (Un objeto marcado con [AsParameters] no sirve aquí: sus propiedades
        // se exigen aunque el record tenga valores por defecto.)
        [Range(1, int.MaxValue)] int page = 1,
        [Range(1, ListProductsHandler.MaxPageSize)] int pageSize = 20)
    {
        var result = await handler.ExecuteAsync(
            new ListProductsQuery(page, pageSize),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(PagedResponse<ProductResponse>.From(result.Value, ProductResponse.FromDto))
            : result.Error.ToProblem(httpContext);
    }

    /// <summary>Obtiene un producto por su identificador.</summary>
    /// <param name="id">Identificador del producto.</param>
    /// <response code="200">El producto solicitado.</response>
    /// <response code="404">No existe un producto con ese identificador.</response>
    public static async Task<Results<Ok<ProductResponse>, ProblemHttpResult>> GetProductById(
        Guid id,
        GetProductByIdHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.ExecuteAsync(new GetProductByIdQuery(id), cancellationToken);

        // Sin el operador "!": Result declara con MemberNotNullWhen que, si
        // IsSuccess es false, Error no es null. El compilador lo sabe.
        return result.IsSuccess
            ? TypedResults.Ok(ProductResponse.FromDto(result.Value))
            : result.Error.ToProblem(httpContext);
    }

    /// <summary>Crea un producto nuevo.</summary>
    /// <remarks>El SKU debe ser único en el catálogo.</remarks>
    /// <response code="201">Producto creado. La cabecera Location apunta a su URL.</response>
    /// <response code="400">Faltan campos obligatorios o tienen un formato imposible de leer.</response>
    /// <response code="409">Ya existe un producto con ese SKU.</response>
    /// <response code="422">Algún dato viola una regla de negocio (formato del SKU, moneda no admitida, precio negativo...).</response>
    public static async Task<Results<CreatedAtRoute<ProductResponse>, ProblemHttpResult>> CreateProduct(
        CreateProductRequest request,
        CreateProductHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Paso 1: contrato HTTP a comando. El "!" está justificado: la
        // validación de .NET ya garantizó que estos campos no son null (si
        // faltaran, la petición no habría llegado hasta aquí).
        var command = new CreateProductCommand(
            request.Sku!,
            request.Name!,
            request.Price!.Value,
            request.Currency!,
            request.InitialStock);

        // Paso 2: invocar el caso de uso.
        var result = await handler.ExecuteAsync(command, cancellationToken);

        if (result.IsFailure)
        {
            return result.Error.ToProblem(httpContext);
        }

        // Paso 3: 201 Created con cabecera Location apuntando al GET del recurso
        // recién creado (por eso los endpoints tienen nombre). REST bien hecho.
        var response = ProductResponse.FromDto(result.Value);

        return TypedResults.CreatedAtRoute(response, nameof(GetProductById), new { id = response.Id });
    }

    /// <summary>Actualiza el precio de un producto.</summary>
    /// <remarks>La moneda debe coincidir con la que ya tiene el producto: cambiar de moneda es otra operación de negocio.</remarks>
    /// <param name="id">Identificador del producto.</param>
    /// <param name="request">Nuevo precio.</param>
    /// <response code="200">Producto con el precio actualizado.</response>
    /// <response code="400">Faltan campos obligatorios.</response>
    /// <response code="404">No existe un producto con ese identificador.</response>
    /// <response code="409">Otra operación modificó el producto mientras tanto, o se intentó cambiar la moneda.</response>
    /// <response code="422">El importe no es válido para la moneda.</response>
    public static async Task<Results<Ok<ProductResponse>, ProblemHttpResult>> UpdateProductPrice(
        Guid id,
        UpdateProductPriceRequest request,
        UpdateProductPriceHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // El Id viene de la RUTA y el resto del CUERPO: aquí se juntan.
        var command = new UpdateProductPriceCommand(id, request.Amount!.Value, request.Currency!);

        var result = await handler.ExecuteAsync(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ProductResponse.FromDto(result.Value))
            : result.Error.ToProblem(httpContext);
    }

    /// <summary>Elimina un producto del catálogo.</summary>
    /// <param name="id">Identificador del producto.</param>
    /// <response code="204">Producto eliminado.</response>
    /// <response code="404">No existe un producto con ese identificador.</response>
    /// <response code="409">El producto aparece en pedidos y no se puede eliminar.</response>
    public static async Task<Results<NoContent, ProblemHttpResult>> DeleteProduct(
        Guid id,
        DeleteProductHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.ExecuteAsync(new DeleteProductCommand(id), cancellationToken);

        // 204 No Content: éxito sin cuerpo, la respuesta estándar de un DELETE
        // que funcionó (no hay nada que devolver: el recurso ya no existe).
        return result.IsSuccess
            ? TypedResults.NoContent()
            : result.Error.ToProblem(httpContext);
    }
}
