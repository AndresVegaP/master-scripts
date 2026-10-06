-- Reporte de ventas agrupadas por mes
-- Migrado de PCK_REPORTES.SP_VENTAS_MENSUALES
SELECT TO_CHAR(v.FECHA, 'YYYY-MM')                AS PERIODO,
       COUNT(*)                                   AS CANTIDAD,
       SUM(PCK_VENTAS.FN_TOTAL_VENTA(v.ID_VENTA)) AS TOTAL
  FROM VENTAS v
 WHERE EXTRACT(YEAR FROM v.FECHA) = :anio
   AND v.ESTADO <> 'ANULADA'
 GROUP BY TO_CHAR(v.FECHA, 'YYYY-MM')
 ORDER BY PERIODO
