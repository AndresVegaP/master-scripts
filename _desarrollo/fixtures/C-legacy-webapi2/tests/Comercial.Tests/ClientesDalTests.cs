using System.Collections.Generic;
using System.Web.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Comercial.Data.Modelos;
using Comercial.Data.Repositorios;

namespace Comercial.Tests
{
    public interface IEjecutorSp
    {
        int Ejecutar(string nombreSp, object parametros);
    }

    [TestClass]
    public class ClientesDalTests
    {
        // SQL de prueba para la base en memoria
        private const string SqlNombreMesFake = @"
            -- Migrado de PCK_UTIL.FN_NOMBRE_MES
            SELECT PCK_TEST.FN_MES_FAKE(:mes) AS NOMBRE FROM TABLA_MESES_TEST";

        [TestMethod]
        public void Obtener_UsaElSpCorrecto()
        {
            var ejecutor = new Mock<IEjecutorSp>();
            ejecutor.Setup(e => e.Ejecutar("PCK_CLIENTES.SP_OBTENER_CLIENTE", It.IsAny<object>())).Returns(1);

            var filas = ejecutor.Object.Ejecutar("PCK_CLIENTES.SP_OBTENER_CLIENTE", new { p_id_cliente = 1 });

            Assert.AreEqual(1, filas);
            ejecutor.Verify(e => e.Ejecutar("PCK_TEST.SP_PREPARAR_DATOS", It.IsAny<object>()), Times.Never);
            Assert.IsFalse(string.IsNullOrEmpty(SqlNombreMesFake));
        }
    }

    // Fake del repositorio: implementa la interfaz de producción, pero vive en el proyecto de pruebas
    public class VentasRepositorioFake : IVentasRepositorio
    {
        public IEnumerable<Venta> ListarPorCliente(int idCliente) => new List<Venta>();

        public Venta Obtener(int idVenta) => new Venta { IdVenta = idVenta, SpOrigen = "PCK_VENTAS.SP_FAKE_OBTENER" };

        public int Registrar(VentaDto venta) => 1;

        public void Anular(int idVenta)
        {
        }

        public int ContarPendientes() => 0;

        public void Reprocesar() => FakeDb.Ejecutar("BEGIN PCK_VENTAS.SP_REPROCESAR_TEST; END;");
    }

    internal static class FakeDb
    {
        public static void Ejecutar(string sql)
        {
        }
    }

    // Controlador de prueba: NO es un endpoint real de la API
    [RoutePrefix("api/fake")]
    public class FakeClientesController : ApiController
    {
        [HttpGet]
        [Route("{id:int}")]
        public IHttpActionResult Obtener(int id) => Ok("PCK_FAKE_TEST.SP_OBTENER");
    }
}
