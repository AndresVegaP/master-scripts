-- Versión anterior del reporte de ventas. YA NO SE USA: ningún código la
-- carga. Se deja para comparar resultados con el DBA y se borrará en la
-- próxima limpieza.
BEGIN
    PCK_REPORTES.SP_VENTAS_POR_MES_V1(:Anio, :p_cursor);
END;
