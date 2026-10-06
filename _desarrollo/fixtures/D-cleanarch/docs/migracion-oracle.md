# Migración de procedimientos Oracle a la API

Seguimiento de los procedimientos almacenados que la API reemplaza. Este
documento es solo de referencia: el estado real lo dice el código.

| Procedimiento | Estado | Dónde |
|---|---|---|
| PCK_PRODUCTOS.SP_LISTAR_PRODUCTOS | Migrado (usa una función de utilidades) | ProductQueries.ListAsync |
| PCK_PRODUCTOS.SP_OBTENER_PRODUCTO | Migrado | ProductRepository.GetByIdAsync |
| PCK_PEDIDOS.SP_REGISTRAR_AUDITORIA | Pendiente (se invoca) | OrderRepository.AddAsync |
| PCK_FACTURACION.SP_EMITIR_FACTURA | Sin empezar | — |
| PCK_FACTURACION.SP_ANULAR_FACTURA | Sin empezar | — |

## Convención de comentarios

Encima del SQL que reemplaza a un procedimiento se escribe:

```csharp
// Migrado de PCK_FACTURACION.SP_EMITIR_FACTURA
```

Y si todavía hay que llamar a la base, se invoca con un bloque anónimo:

```sql
BEGIN PCK_FACTURACION.SP_EMITIR_FACTURA(:Id); END;
```

## Pendientes

- Revisar con el DBA si `PRC_REPROCESAR_COLA` se puede dar de baja.
- `PKG_LEGADO.SP_SINCRONIZAR_STOCK` no se migra: se apaga junto con el ERP viejo.
