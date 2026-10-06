using System.Data;
using Dapper;
using Dapper.Oracle;
using Ventas.Api.Data.Interfaces;
using Ventas.Api.Models;

namespace Ventas.Api.Data.Repositories;

public class ProductosRepository : BaseRepository, IProductosRepository
{
    // Marcas que llegan en los codigos importados desde planillas (no son comentarios)
    private const string MarcaInicio = "/*";
    private const string MarcaRuta = "//";

    public ProductosRepository(IConfiguration configuration) : base(configuration)
    {
    }

    public async Task<IEnumerable<Producto>> ListarAsync()
    {
        const string sql = @"
            -- Migrado de PCK_PRODUCTOS.SP_LISTAR_PRODUCTOS
            SELECT p.ID_PRODUCTO, p.CODIGO, p.DESCRIPCION, p.PRECIO_BASE
              FROM PRODUCTOS p
             WHERE p.ACTIVO = 'S'
               AND p.ORIGEN <> 'PCK_CATALOGO.SP_IMPORTAR'
             ORDER BY p.DESCRIPCION";

        using var conn = CreateConnection();
        return await conn.QueryAsync<Producto>(sql);
    }

    /// <summary>
    /// Lista las categorias activas. Reemplaza a PCK_PRODUCTOS.SP_LISTAR_CATEGORIAS.
    /// </summary>
    public async Task<IEnumerable<Categoria>> ListarCategoriasAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<Categoria>(
            @"SELECT ID_CATEGORIA, NOMBRE
                FROM CATEGORIAS
               WHERE ACTIVA = 'S'
               ORDER BY NOMBRE");
    }

    public async Task<Producto?> ObtenerPorCodigoAsync(string codigo)
    {
        var codigoLimpio = codigo.Replace(MarcaInicio, string.Empty).Replace(MarcaRuta, string.Empty);

        var p = new OracleDynamicParameters();
        p.Add("pCodigo", codigoLimpio, OracleMappingType.Varchar2, ParameterDirection.Input);
        p.Add("pCursor", dbType: OracleMappingType.RefCursor, direction: ParameterDirection.Output);

        var resultado = await QuerySpAsync<Producto>(StoredProcedures.ObtenerProducto, p);
        return resultado.FirstOrDefault();
    }

    public async Task<IEnumerable<Producto>> BuscarAsync(string filtro, int maximo)
    {
        // Oracle 10g no tiene FETCH FIRST: se limita con ROWNUM
        const string sql = @"
            SELECT *
              FROM (SELECT p.ID_PRODUCTO, p.CODIGO, p.DESCRIPCION, p.PRECIO_BASE
                      FROM PRODUCTOS p
                     WHERE UPPER(p.DESCRIPCION) LIKE '%' || UPPER(:filtro) || '%'
                     ORDER BY p.DESCRIPCION)
             WHERE ROWNUM <= :maximo";

        using var conn = CreateConnection();
        return await conn.QueryAsync<Producto>(sql, new { filtro, maximo });
    }

    public async Task<decimal?> ObtenerPrecioAsync(int idProducto, string lista)
    {
        // La lista de precios vive en la base comercial (db link)
        using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<decimal?>(
            "SELECT PCK_PRECIOS.FN_PRECIO_LISTA@DBL_COMERCIAL(:idProducto, :lista) FROM DUAL",
            new { idProducto, lista });
    }

    // Pendiente: todavia no hay endpoint que descontinue productos
    public Task<int> DescontinuarAsync(int idProducto)
    {
        return ExecuteSpAsync("PCK_PRODUCTOS.SP_DESCONTINUAR", new { pIdProducto = idProducto });
    }
}
