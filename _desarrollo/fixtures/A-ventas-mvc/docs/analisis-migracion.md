# Analisis de migracion - Modulo Ventas

Documento de trabajo del equipo. No es codigo de la API.

## Inventario de procedimientos

| Paquete | Procedimiento | Endpoint destino | Estado |
|---|---|---|---|
| PCK_VENTAS | SP_LISTAR_VENTAS | GET /api/Ventas | Migrado a consulta (StringBuilder) |
| PCK_VENTAS | SP_OBTENER_VENTA | GET /api/Ventas/{id} | Se sigue llamando al SP |
| PCK_VENTAS | SP_CERRAR_CAJA | (sin endpoint) | Pendiente |
| PCK_CLIENTES | SP_LISTAR_CLIENTES | GET /api/Clientes | Migrado, quedan funciones hijas |
| PCK_REPORTES | SP_VENTAS_MENSUALES | GET /api/Reportes/ventas-mensuales | Migrado (archivo .sql) |
| PCK_FACTURACION | SP_EMITIR_FACTURA | POST /api/facturas | No iniciado |

Llamadas pendientes de revisar: PCK_VENTAS.SP_CERRAR_CAJA(:pIdCaja); PCK_FACTURACION.SP_EMITIR_FACTURA(:pIdVenta);

## Propuesta para FN_TOTAL_VENTA

Se propone reemplazar la funcion por la siguiente consulta:

```sql
-- Migrado de PCK_VENTAS.FN_TOTAL_VENTA
SELECT SUM(d.CANTIDAD * d.PRECIO) + PCK_IMPUESTOS.FN_IVA(:idVenta) AS TOTAL
  FROM VENTAS_DETALLE d
 WHERE d.ID_VENTA = :idVenta
```

## Ejemplo de endpoint futuro (referencia)

```csharp
[HttpPost("~/api/facturas")]
public Task<int> Emitir(FacturaDto dto)
    => ExecuteSpAsync("PCK_FACTURACION.SP_EMITIR_FACTURA", dto);
```
