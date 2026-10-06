using Inventario.Application.Abstractions;
using Inventario.Application.Modelos;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Inventario.Application.Productos.Commands;

// ---------------------------------------------------------------
// Alta de producto
// ---------------------------------------------------------------
public sealed record CrearProductoCommand(string Descripcion, string Categoria, string UnidadCodigo, decimal PrecioBase) : IRequest<Guid>;

public sealed class CrearProductoCommandHandler : IRequestHandler<CrearProductoCommand, Guid>
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CrearProductoCommandHandler> _logger;

    public CrearProductoCommandHandler(IUnitOfWork uow, ILogger<CrearProductoCommandHandler> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<Guid> Handle(CrearProductoCommand request, CancellationToken cancellationToken)
    {
        // Validaciones que antes hacia el paquete en la base de datos
        if (string.IsNullOrWhiteSpace(request.Descripcion))
            throw new ArgumentException("La descripcion es obligatoria", nameof(request));
        if (request.PrecioBase <= 0)
            throw new ArgumentException("El precio base debe ser mayor a cero", nameof(request));

        var producto = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = await _uow.Productos.GenerarSkuAsync(request.Categoria),
            Descripcion = request.Descripcion.Trim(),
            Categoria = request.Categoria,
            UnidadBase = request.UnidadCodigo,
            PrecioBase = request.PrecioBase
        };

        await _uow.Productos.InsertarAsync(producto);
        await _uow.CommitAsync(cancellationToken);

        _logger.LogInformation("Producto {Sku} creado con Id {Id}", producto.Sku, producto.Id);
        return producto.Id;
    }
}

// ---------------------------------------------------------------
// Baja logica
// ---------------------------------------------------------------
public sealed record DesactivarProductoCommand(Guid Id) : IRequest;

public sealed class DesactivarProductoCommandHandler : IRequestHandler<DesactivarProductoCommand>
{
    private readonly IUnitOfWork _uow;

    public DesactivarProductoCommandHandler(IUnitOfWork uow) => _uow = uow;

    public async Task Handle(DesactivarProductoCommand request, CancellationToken cancellationToken)
    {
        await _uow.Productos.DesactivarAsync(request.Id);
        await _uow.Stock.RecalcularDisponibleAsync(request.Id);
        await _uow.CommitAsync(cancellationToken);
    }
}

// ---------------------------------------------------------------
// Baja fisica: ningun endpoint envia este comando todavia
// ---------------------------------------------------------------
public sealed record EliminarProductoCommand(Guid Id) : IRequest;

public sealed class EliminarProductoCommandHandler : IRequestHandler<EliminarProductoCommand>
{
    private readonly IUnitOfWork _uow;

    public EliminarProductoCommandHandler(IUnitOfWork uow) => _uow = uow;

    public async Task Handle(EliminarProductoCommand request, CancellationToken cancellationToken)
    {
        await _uow.Productos.EliminarAsync(request.Id);
        await _uow.CommitAsync(cancellationToken);
    }
}
