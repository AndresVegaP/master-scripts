namespace Ventas.Api.Models;

public class Venta
{
    public int IdVenta { get; set; }
    public string Folio { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public int IdCliente { get; set; }
    public decimal MontoTotal { get; set; }
    public string Estado { get; set; } = string.Empty;
}

public class VentaDetalle : Venta
{
    public List<LineaVenta> Lineas { get; set; } = new();
}

public class LineaVenta
{
    public int IdProducto { get; set; }
    public decimal Cantidad { get; set; }
    public decimal Precio { get; set; }
}

public class VentaResumen
{
    public int IdVenta { get; set; }
    public string Folio { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public decimal MontoTotal { get; set; }
    public string RazonSocial { get; set; } = string.Empty;
}

public class VentaDto
{
    public string Serie { get; set; } = "A";
    public string Folio { get; set; } = string.Empty;
    public int IdCliente { get; set; }
    public decimal Monto { get; set; }
    public DateTime Fecha { get; set; }
    public string Estado { get; set; } = "VIGENTE";
}

public class ResumenDiario
{
    public DateTime Dia { get; set; }
    public int Cantidad { get; set; }
    public decimal Total { get; set; }
}

public class MedioPago
{
    public int IdMedioPago { get; set; }
    public string Descripcion { get; set; } = string.Empty;
}

public class Cliente
{
    public int IdCliente { get; set; }
    public string RazonSocial { get; set; } = string.Empty;
    public string Rut { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Observacion { get; set; }
    public decimal Saldo { get; set; }
}

public class PagoDto
{
    public decimal Monto { get; set; }
    public string MedioPago { get; set; } = "EFECTIVO";
}

public class Producto
{
    public int IdProducto { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal PrecioBase { get; set; }
}

public class Categoria
{
    public int IdCategoria { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public class VentaMensual
{
    public string Periodo { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public decimal Total { get; set; }
}

public class SolicitudReporte
{
    public string Tipo { get; set; } = string.Empty;
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
}
