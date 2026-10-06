/*
 * Ventas del año agrupadas por mes (solo pedidos confirmados).
 * Migrado de PCK_REPORTES.SP_VENTAS_POR_MES
 * El procedimiento llenaba una tabla temporal y devolvía un REF CURSOR;
 * aquí es una sola consulta.
 */
SELECT EXTRACT(MONTH FROM o.PlacedAt)                         AS Mes,
       PCK_UTIL.FN_NOMBRE_MES(EXTRACT(MONTH FROM o.PlacedAt))  AS NombreMes,
       COUNT(DISTINCT o.Id)                                   AS Pedidos,
       SUM(l.UnitPrice * l.Quantity)                          AS Total
FROM Orders o
JOIN OrderLines l ON l.OrderId = o.Id
WHERE o.Status = 'Placed'
  AND EXTRACT(YEAR FROM o.PlacedAt) = :Anio
GROUP BY EXTRACT(MONTH FROM o.PlacedAt)
ORDER BY Mes
