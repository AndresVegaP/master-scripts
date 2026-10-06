namespace Inventario.Application.Modelos;

public sealed record FiltroProductos(string? Texto, string? Categoria, int Pagina = 1, int Tamanio = 50);

public sealed record ProductoDto(Guid Id, string Sku, string SkuFormateado, string Descripcion, string Estado, string EstadoDescripcion);

public sealed record ProductoResumenDto(Guid Id, string Sku, string Descripcion, string Unidad, decimal Stock);

public sealed record MovimientoKardexDto(DateTime Fecha, string Tipo, decimal Cantidad, decimal Saldo, string Documento);

public sealed record PrecioDto(Guid ProductoId, decimal PrecioBase, decimal PrecioFinal, string Moneda);

public sealed record UnidadDto(string Codigo, string Descripcion, decimal Factor);

public sealed record StockDto(string Sku, decimal Disponible, decimal Reservado, string Estado);

public sealed record Almacen(string Codigo, string Nombre, string Estado);

public sealed record Ubicacion(string Codigo, string AlmacenCodigo, string Pasillo, string Nivel);

public sealed class Producto
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public string UnidadBase { get; set; } = string.Empty;
    public decimal PrecioBase { get; set; }
}
