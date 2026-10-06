-- Version anterior del reporte mensual (ya no se usa; se deja como respaldo)
SELECT LEVEL                                       AS MES,
       PCK_REPORTES.FN_VENTAS_MES(:anio, LEVEL)    AS TOTAL
  FROM DUAL
CONNECT BY LEVEL <= 12
