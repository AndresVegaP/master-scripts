using System.Data;
using Inventario.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Inventario.Infrastructure.Persistence;

/// <summary>
/// Implementacion de IUnitOfWork: los repositorios se crean bajo demanda y comparten transaccion.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly IDbConnection _conexion;
    private readonly ILoggerFactory _loggerFactory;
    private IDbTransaction? _transaccion;

    private IProductoRepository? _productos;
    private IStockRepository? _stock;
    private IKardexRepository? _kardex;
    private IAlmacenRepository? _almacenes;
    private IUbicacionRepository? _ubicaciones;
    private IAuditoriaRepository? _auditoria;

    public UnitOfWork(IDbConnection conexion, ILoggerFactory loggerFactory)
    {
        _conexion = conexion;
        _loggerFactory = loggerFactory;
        _conexion.Open();
        _transaccion = _conexion.BeginTransaction();
    }

    public IProductoRepository Productos => _productos ??= new ProductoRepository(_conexion, _transaccion);

    public IStockRepository Stock => _stock ??= new StockRepository(_conexion, _transaccion, _loggerFactory.CreateLogger<StockRepository>());

    public IKardexRepository Kardex => _kardex ??= new KardexRepository(_conexion, _transaccion);

    public IAlmacenRepository Almacenes => _almacenes ??= new AlmacenRepository(_conexion, _transaccion);

    public IUbicacionRepository Ubicaciones => _ubicaciones ??= new UbicacionRepository(_conexion, _transaccion);

    public IAuditoriaRepository Auditoria => _auditoria ??= new AuditoriaRepository(_conexion, _transaccion);

    public Task CommitAsync(CancellationToken ct = default)
    {
        _transaccion?.Commit();
        _transaccion = _conexion.BeginTransaction();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _transaccion?.Dispose();
        _conexion.Dispose();
    }
}
