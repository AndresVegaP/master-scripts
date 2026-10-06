-- Migrado de PCK_REPORTES.SP_VENTAS_POR_MES
-- (copia vieja que quedó en la salida de compilación)
SELECT EXTRACT(MONTH FROM o.PlacedAt) AS Mes,
       PCK_UTIL.FN_NOMBRE_MES_OLD(EXTRACT(MONTH FROM o.PlacedAt)) AS NombreMes
FROM Orders o
WHERE EXTRACT(YEAR FROM o.PlacedAt) = :Anio
GROUP BY EXTRACT(MONTH FROM o.PlacedAt)
