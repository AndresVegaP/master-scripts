-- Consulta de análisis: qué objetos dependen de los paquetes que migramos.
-- Se ejecuta a mano en SQL Developer; la API no la usa.
SELECT d.NAME, d.TYPE, d.REFERENCED_NAME
FROM ALL_DEPENDENCIES d
WHERE d.REFERENCED_NAME IN ('PCK_PRODUCTOS', 'PCK_PEDIDOS', 'PCK_FACTURACION')
ORDER BY d.REFERENCED_NAME, d.NAME;

-- Prueba rápida de la función de totales de factura.
SELECT PCK_FACTURACION.FN_TOTAL_FACTURA(:Id) FROM DUAL;

BEGIN
    PCK_FACTURACION.SP_ANULAR_FACTURA(:Id, 'PRUEBA');
END;
