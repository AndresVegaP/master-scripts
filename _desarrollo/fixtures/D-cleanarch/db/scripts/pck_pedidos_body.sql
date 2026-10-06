-- Copia del cuerpo del paquete de pedidos tal como está en producción.
-- Script de referencia del DBA: no lo carga ningún código de la API.
CREATE OR REPLACE PACKAGE BODY PCK_PEDIDOS AS

    PROCEDURE SP_INSERTAR_PEDIDO(p_id IN VARCHAR2, p_moneda IN VARCHAR2) IS
    BEGIN
        INSERT INTO ORDERS (ID, CURRENCY, STATUS, CREATEDAT, VERSION)
        VALUES (p_id, p_moneda, 'Draft', SYSTIMESTAMP, 0);

        PCK_PEDIDOS.SP_REGISTRAR_AUDITORIA(p_id, 'ALTA');
        PRC_RECALCULAR_STOCK(p_id);
    END SP_INSERTAR_PEDIDO;

    FUNCTION FN_ESTADO_DESC(p_estado IN VARCHAR2) RETURN VARCHAR2 IS
    BEGIN
        RETURN CASE p_estado WHEN 'Placed' THEN 'Confirmado' WHEN 'Cancelled' THEN 'Anulado' ELSE 'Borrador' END;
    END FN_ESTADO_DESC;

END PCK_PEDIDOS;
/
