using CleanArchitecture.Infrastructure.Persistence;

namespace CleanArchitecture.IntegrationTests.Persistence;

/// <summary>Protege los nombres de procedimientos que la API invoca por nombre.</summary>
public class OracleProcedureNamesTests
{
    // En el ambiente de QA existe un paquete de mocks que responde siempre lo
    // mismo; estos tests lo usan para no depender de datos reales.
    private const string MockStockSql = "BEGIN PCK_PRUEBAS.SP_MOCK_STOCK(:Id); END;";

    [Fact]
    public void EliminarProducto_ApuntaAlPaqueteDeProductos()
    {
        Assert.Equal("PCK_PRODUCTOS.SP_ELIMINAR_PRODUCTO", ProcedimientosOracle.EliminarProducto);
    }

    [Fact]
    public void EncolarCorreo_ApuntaAlPaqueteDeNotificaciones()
    {
        Assert.Equal("PKG_NOTIFICACIONES.SP_ENCOLAR_CORREO", ProcedimientosOracle.EncolarCorreo);
    }

    [Fact]
    public void BloqueDeMock_EsUnBloqueAnonimo()
    {
        Assert.StartsWith("BEGIN", MockStockSql, StringComparison.Ordinal);
        Assert.EndsWith("END;", MockStockSql, StringComparison.Ordinal);
    }
}
