-- Migrado de PCK_VENTAS.SP_LISTAR_POR_CLIENTE
-- Nota: el SP original abría un cursor explícito; aquí es un SELECT simple
SELECT v.ID_VENTA,
       v.FECHA,
       v.TOTAL,
       PCK_UTIL.FN_FORMATEAR_RUT(c.RUT) AS RUT_FORMATEADO
  FROM VENTAS v
  JOIN CLIENTES c ON c.ID_CLIENTE = v.ID_CLIENTE
 WHERE v.ID_CLIENTE = :idCliente
   AND v.ESTADO <> 'A'
 ORDER BY v.FECHA DESC
