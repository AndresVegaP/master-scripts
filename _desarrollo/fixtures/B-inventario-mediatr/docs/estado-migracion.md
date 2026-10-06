# Estado de migracion - Inventario

| Procedimiento | Estado | Endpoint .NET |
|---|---|---|
| PCK_INVENTARIO.SP_LISTAR_PRODUCTOS | Migrado a SQL embebido | GET /api/v1/productos |
| PCK_INVENTARIO.SP_KARDEX_PRODUCTO | Migrado | GET /api/v1/productos/{id}/kardex |
| PCK_INVENTARIO.SP_TRANSFERIR_STOCK | Pendiente | - |
| PCK_ALMACEN.SP_CERRAR_ALMACEN | Llamada dinamica (configuracion) | POST /api/v1/almacenes/{codigo}/cierre |
| PCK_COSTOS.SP_RECALCULAR_COSTO_PROMEDIO | Job sin programar | - |

Notas:

- `PCK_UTIL.FN_UNIDAD_MEDIDA` se reescribio como consulta en ProductoRepository.
- No usar `PCK_LEGADO.SP_AJUSTE_MANUAL` (obsoleto, solo se conserva como codigo de origen en auditoria).
- Ejemplo de llamada antigua: `BEGIN PCK_INVENTARIO.SP_AJUSTAR_STOCK(:id, :cant, :motivo, :saldo); END;`
