-- =============================================================
-- Listado paginado de productos activos con stock disponible
-- Migrado de PCK_INVENTARIO.SP_LISTAR_PRODUCTOS
-- Nota: descripciones con apostrofe (') llegan escapadas desde el front
-- =============================================================
SELECT *
  FROM (SELECT p.ID                                     AS Id,
               p.SKU                                    AS Sku,
               p.DESCRIPCION                            AS Descripcion,
               PCK_UTIL.FN_UNIDAD_MEDIDA(p.UNIDAD_BASE) AS Unidad,
               NVL(s.DISPONIBLE, 0)                     AS Stock,
               ROWNUM                                   AS RN
          FROM PRODUCTOS p
          LEFT JOIN STOCK_PRODUCTO s ON s.PRODUCTO_ID = p.ID
         WHERE p.ESTADO = 'A'
           AND (:texto IS NULL OR UPPER(p.DESCRIPCION) LIKE '%' || UPPER(:texto) || '%')
           AND (:categoria IS NULL OR p.CATEGORIA = :categoria)
           AND ROWNUM <= :hasta)
 WHERE RN > :desde
