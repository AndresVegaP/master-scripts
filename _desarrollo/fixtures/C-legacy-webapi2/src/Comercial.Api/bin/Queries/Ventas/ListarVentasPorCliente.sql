-- Copia de salida del build (versión vieja del recurso). No es la fuente.
-- Migrado de PCK_VENTAS.SP_LISTAR_POR_CLIENTE
SELECT v.ID_VENTA,
       PCK_BIN.FN_COPIA_OBSOLETA(c.RUT) AS RUT
  FROM VENTAS v
  JOIN CLIENTES c ON c.ID_CLIENTE = v.ID_CLIENTE
 WHERE v.ID_CLIENTE = :idCliente
