using System;
using System.Collections.Generic;

namespace Comercial.Data.Modelos
{
    public class Cliente
    {
        public int IdCliente { get; set; }
        public string Nombre { get; set; }
        public string Email { get; set; }
        public string Rut { get; set; }
    }

    public class ResumenCliente
    {
        public decimal Saldo { get; set; }
        public string Categoria { get; set; }
    }

    public class Producto
    {
        public int IdProducto { get; set; }
        public string Nombre { get; set; }
        public decimal PrecioBase { get; set; }
        public decimal Precio { get; set; }
    }

    public class Venta
    {
        public int IdVenta { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Total { get; set; }
        public string SpOrigen { get; set; }
        public string Cliente { get; set; }
        public string RutFormateado { get; set; }
    }

    public class VentaDto
    {
        public int IdCliente { get; set; }
        public decimal Total { get; set; }
        public List<DetalleVentaDto> Detalle { get; set; } = new List<DetalleVentaDto>();
    }

    public class DetalleVentaDto
    {
        public int IdProducto { get; set; }
        public int Cantidad { get; set; }
    }

    public class VentaMensual
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string NombreMes { get; set; }
        public decimal Total { get; set; }
    }

    public class TopCliente
    {
        public int IdCliente { get; set; }
        public string Nombre { get; set; }
        public decimal Total { get; set; }
        public string Obs { get; set; }
    }

    public class VentaHistorica
    {
        public int IdVenta { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Total { get; set; }
    }

    public class ResumenHistorico
    {
        public int IdCliente { get; set; }
        public decimal Total { get; set; }
        public string Etiqueta { get; set; }
        public string Observacion { get; set; }
        public string Origen { get; set; }
    }

    public class PrecioCalculado
    {
        public int IdProducto { get; set; }
        public decimal Neto { get; set; }
        public decimal Iva { get; set; }
    }

    public class PrecioSegmento
    {
        public string Segmento { get; set; }
        public int IdProducto { get; set; }
        public decimal Precio { get; set; }
    }
}
