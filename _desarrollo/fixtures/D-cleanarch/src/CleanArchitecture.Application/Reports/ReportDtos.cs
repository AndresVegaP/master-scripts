namespace CleanArchitecture.Application.Reports;

/// <summary>Ventas de un mes.</summary>
/// <param name="Mes">Número de mes (1 a 12).</param>
/// <param name="NombreMes">Nombre del mes en español.</param>
/// <param name="Pedidos">Pedidos confirmados en el mes.</param>
/// <param name="Total">Importe total vendido.</param>
public sealed record VentaMensualDto(int Mes, string NombreMes, int Pedidos, decimal Total);

/// <summary>Producto con poco stock disponible.</summary>
public sealed record StockBajoDto(string Sku, string Name, string Nota, int Disponible);

/// <summary>Producto del ranking de ventas.</summary>
public sealed record TopProductoDto(string Sku, string Name, int Unidades, decimal Total);

/// <summary>Moneda admitida por el catálogo.</summary>
public sealed record MonedaDto(string Codigo, int Decimales, string? ProcesoOrigen);
