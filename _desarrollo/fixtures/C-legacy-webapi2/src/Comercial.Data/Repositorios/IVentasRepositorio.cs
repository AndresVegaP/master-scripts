using System.Collections.Generic;
using Comercial.Data.Modelos;

namespace Comercial.Data.Repositorios
{
    /// <summary>
    /// Contrato del repositorio de ventas.
    /// </summary>
    public interface IVentasRepositorio
    {
        IEnumerable<Venta> ListarPorCliente(int idCliente);

        Venta Obtener(int idVenta);

        int Registrar(VentaDto venta);

        void Anular(int idVenta);

        int ContarPendientes();

        void Reprocesar();
    }
}
