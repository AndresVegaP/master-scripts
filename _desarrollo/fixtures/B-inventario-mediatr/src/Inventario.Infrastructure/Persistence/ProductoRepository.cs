using System.Data;
using Dapper;
using Inventario.Application.Abstractions;
using Inventario.Application.Modelos;
using Inventario.Infrastructure.Sql;

namespace Inventario.Infrastructure.Persistence;

// Repositorio de productos sobre Oracle 10g.
// Ojo: Dapper no escapa el caracter " en los alias; usar alias sin comillas
public sealed class ProductoRepository : Repository<Producto>, IProductoRepository
{
    // Migrado de PCK_UTIL.FN_UNIDAD_MEDIDA: la funcion devolvia una sola unidad, ahora se listan todas
    private const string SqlUnidades = """
        SELECT u.CODIGO      AS Codigo,
               u.DESCRIPCION AS Descripcion,
               PCK_UTIL.FN_FACTOR_CONVERSION(u.CODIGO, p.UNIDAD_BASE) AS Factor
          FROM UNIDADES_MEDIDA u
          JOIN PRODUCTOS p ON p.UNIDAD_BASE = u.CODIGO_BASE
         WHERE p.ID = :id
        """;

    public ProductoRepository(IDbConnection conexion, IDbTransaction? transaccion)
        : base(conexion, transaccion)
    {
    }

    protected override string NombreTabla => "PRODUCTOS";

    public async Task<ProductoDto?> ObtenerPorIdAsync(Guid id)
    {
        const string sql = """
            SELECT p.ID AS Id,
                   p.SKU AS Sku,
                   pck_util.fn_formatear_sku(p.sku) AS SkuFormateado,
                   NVL(p.DESCRIPCION, '--') AS Descripcion, p.ESTADO AS Estado, FN_ESTADO_PRODUCTO(p.ESTADO) AS EstadoDescripcion
              FROM PRODUCTOS p
             WHERE p.ID = :id
            """;
        return await Connection.QuerySingleOrDefaultAsync<ProductoDto>(sql, new { id }, Transaction);
    }

    public async Task<IReadOnlyList<ProductoResumenDto>> ListarAsync(FiltroProductos filtro)
    {
        var sql = SqlLoader.Load("ListarProductos.sql");
        var filas = await Connection.QueryAsync<ProductoResumenDto>(sql, new
        {
            texto = filtro.Texto,
            categoria = filtro.Categoria,
            desde = (filtro.Pagina - 1) * filtro.Tamanio,
            hasta = filtro.Pagina * filtro.Tamanio
        }, Transaction);
        return filas.ToList();
    }

    public async Task<PrecioDto?> ObtenerPrecioAsync(Guid id, DateTime fecha)
    {
        var sql = """
            SELECT p.ID AS ProductoId,
                   l.PRECIO_BASE AS PrecioBase,
                   "PCK_PRECIOS"."FN_APLICAR_DESCUENTO"(p.ID, :fecha) AS PrecioFinal,
                   l.MONEDA AS Moneda
              FROM PRODUCTOS p
              JOIN PRC_LISTA_PRECIOS l ON l.PRODUCTO_ID = p.ID
             WHERE p.ID = :id
               AND :fecha BETWEEN l.VIGENCIA_DESDE AND NVL(l.VIGENCIA_HASTA, :fecha)
            """;
        return await Connection.QuerySingleOrDefaultAsync<PrecioDto>(sql, new { id, fecha }, Transaction);
    }

    public async Task<IReadOnlyList<UnidadDto>> ObtenerUnidadesAsync(Guid id)
    {
        var filas = await Connection.QueryAsync<UnidadDto>(SqlUnidades, new { id }, Transaction);
        return filas.ToList();
    }

    public async Task<string> GenerarSkuAsync(string categoria)
    {
        var p = new DynamicParameters();
        p.Add("p_categoria", categoria);
        p.Add("p_sku", dbType: DbType.String, size: 20, direction: ParameterDirection.Output);
        await Connection.ExecuteAsync("PRC_GENERAR_CODIGO_SKU", p, Transaction, commandType: CommandType.StoredProcedure); // PRC_GENERAR_CODIGO_SKU arma el correlativo por categoria
        return p.Get<string>("p_sku");
    }

    /// <summary>
    /// Alta de producto. Migrado de PCK_INVENTARIO.SP_CREAR_PRODUCTO (las validaciones pasaron al handler).
    /// </summary>
    public Task InsertarAsync(Producto producto)
    {
        const string sql = """
            INSERT INTO PRODUCTOS (ID, SKU, DESCRIPCION, CATEGORIA, UNIDAD_BASE, PRECIO_BASE, ESTADO, FECHA_ALTA)
            VALUES (:Id, :Sku, :Descripcion, :Categoria, :UnidadBase, :PrecioBase, 'A', SYSDATE)
            """;
        return Connection.ExecuteAsync(sql, producto, Transaction);
    }

    public Task DesactivarAsync(Guid id)
    {
        const string sql = """
            UPDATE PRODUCTOS
               SET ESTADO = 'I',
                   FECHA_BAJA = SYSDATE,
                   USUARIO_BAJA = PCK_SEGURIDAD.FN_USUARIO_ACTUAL()
             WHERE ID = :id
            """;
        return Connection.ExecuteAsync(sql, new { id }, Transaction);
    }

    public Task EliminarAsync(Guid id)
        => Connection.ExecuteAsync("PCK_INVENTARIO.SP_ELIMINAR_PRODUCTO", new { p_id = id }, Transaction,
            commandType: CommandType.StoredProcedure);

    // Pendiente: exponer en el modulo de reportes (hoy nadie lo invoca)
    public async Task<IReadOnlyList<PrecioDto>> ObtenerHistoricoPreciosAsync(Guid id)
    {
        var filas = await Connection.QueryAsync<PrecioDto>("""
            SELECT h.PRODUCTO_ID AS ProductoId, h.PRECIO_BASE AS PrecioBase, h.PRECIO_FINAL AS PrecioFinal, h.MONEDA AS Moneda
              FROM TABLE(PCK_PRECIOS.FN_HISTORICO_PRECIOS(:id)) h
            """, new { id }, Transaction);
        return filas.ToList();
    }
}
