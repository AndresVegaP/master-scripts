# Migración de SPs Oracle 10g a la API Comercial

Estado de los paquetes al cierre del sprint 14.

| Endpoint | SP original | Estado |
|----------|-------------|--------|
| GET api/clientes/{id} | PCK_CLIENTES.SP_OBTENER_CLIENTE | Pendiente |
| PUT api/clientes/{id} | PCK_CLIENTES.SP_ACTUALIZAR | Migrado |
| GET api/ventas/{id} | PCK_VENTAS.SP_OBTENER_VENTA | Migrado |
| (ninguno) | PCK_DOCS.SP_SOLO_DOCUMENTADO | Descartado |

## Notas

- `PCK_STOCK.SP_VALIDAR` se dejará de usar cuando el stock pase al microservicio.
- Ejemplo de llamada: `BEGIN PCK_DOCS.SP_EJEMPLO(:p_id); END;`
