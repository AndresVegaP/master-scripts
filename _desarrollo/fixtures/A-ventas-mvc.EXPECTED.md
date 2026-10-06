# EXPECTED - Fixture A-ventas-mvc

Fixture: `A-ventas-mvc/` (ASP.NET Core 8 MVC + Dapper + Dapper.Oracle, Oracle 10g).

Convenciones de este archivo:
- Rutas de archivo relativas a la raiz del fixture (`A-ventas-mvc/`), con `/`.
- Columna `Hijo` = `-` cuando no aplica.
- Linea de filas DIRECTO / DIRECTO_EN_QUERY = linea donde aparece el nombre del SP (o la constante / interpolacion que lo produce).
- Linea de filas HIJO = linea donde aparece el nombre del hijo dentro de la consulta migrada.
- Linea de filas HIJO_N2 = linea donde aparece el nieto (en la consulta que migra al hijo).
- Linea de filas MIGRADO_LISTO / SOLO_COMENTARIO = linea del comentario/atributo marcador.
- `ANY` = accion sin atributo de verbo en controlador con routing por atributos.

## Endpoints

25 endpoints (23 con SP, 2 sin SP).

1. GET /api/Ventas  (Handler: VentasController.Listar)
2. GET /api/Ventas/{id}  (Handler: VentasController.Obtener)
3. GET /api/Ventas/{id}/detalle  (Handler: VentasController.ObtenerDetalle)
4. POST /api/Ventas  (Handler: VentasController.Registrar)
5. PUT /api/Ventas/{id}  (Handler: VentasController.Actualizar)
6. DELETE /api/Ventas/{id}  (Handler: VentasController.Anular)
7. ANY /api/Ventas/sincronizar  (Handler: VentasController.Sincronizar)
8. GET /api/v2/ventas/resumen  (Handler: VentasController.Resumen)
9. GET /api/Ventas/medios-pago  (Handler: VentasController.ListarMediosPago)
10. GET /api/Clientes  (Handler: ClientesController.Listar)
11. GET /api/Clientes/{id}  (Handler: ClientesController.Obtener)
12. GET /api/Clientes/{id}/saldo  (Handler: ClientesController.ObtenerSaldo)
13. POST /api/Clientes/{id}/pagos  (Handler: ClientesController.RegistrarPago)
14. GET /api/Clientes/buscar  (Handler: ClientesController.Buscar)
15. POST /api/Clientes/buscar  (Handler: ClientesController.Buscar)
16. DELETE /api/Clientes  (Handler: ClientesController.EliminarInactivos)
17. GET /api/Productos  (Handler: ProductosController.Listar)
18. GET /api/Productos/categorias  (Handler: ProductosController.ListarCategorias)
19. GET /api/Productos/PorCodigo/{codigo}  (Handler: ProductosController.ObtenerPorCodigoAsync)
20. GET /api/Productos/buscar  (Handler: ProductosController.Buscar)
21. GET /api/Productos/{id}/precio  (Handler: ProductosController.ObtenerPrecio)
22. POST /api/Reportes/generar  (Handler: ReportesController.Generar)
23. GET /api/Reportes/ventas-mensuales/{anio}  (Handler: ReportesController.VentasMensuales)
24. GET /api/Salud/Ping  (Handler: SaludController.Ping)
25. GET /api/Salud/version  (Handler: SaludController.Version)

Notas de ruteo:
- Ventas/Clientes/Productos/Reportes NO declaran `[Route]`: lo heredan de `BaseApiController` (`src/Ventas.Api/Controllers/BaseApiController.cs:10`, `[Route("api/[controller]")]`). `[controller]` = nombre de la clase sin el sufijo `Controller`, conservando mayusculas (`Ventas`, `Clientes`, ...).
- `SaludController` declara su propio `[Route("api/[controller]")]` (linea 7) y deriva de `ControllerBase`.
- #7: `[Route("sincronizar")]` sin verbo => `ANY`.
- #8: `[HttpGet("~/api/v2/ventas/resumen")]` es ruta absoluta: NO lleva el prefijo `api/Ventas`.
- #14/#15: una sola accion con `[HttpGet("buscar")]` + `[HttpPost("buscar")]` => dos endpoints con las mismas filas.
- #19: `[HttpGet("[action]/{codigo}")]` + `[ActionName("PorCodigo")]` => `[action]` = `PorCodigo` (no `ObtenerPorCodigoAsync`).
- #23: `{anio:int?}` => `{anio}` (se quitan restriccion y marca de opcional). #2/#3/#6/#11/#12/#13/#21: `{id:int}` => `{id}`.
- #24: `[HttpGet("[action]")]` en `Ping()` => `Ping`.
- `VentasController.RecalcularTotales` tiene `[NonAction]` => NO es endpoint.

## Filas esperadas

Total: 27 filas (DIRECTO 12, DIRECTO_EN_QUERY 3, MIGRADO_LISTO 6, HIJO 4, HIJO_N2 1, SOLO_COMENTARIO 1).

| Endpoint | SP | Hijo | Tipo | Archivo:linea | Notas |
|---|---|---|---|---|---|
| GET /api/Ventas | PCK_VENTAS.SP_LISTAR_VENTAS | - | MIGRADO_LISTO | src/Ventas.Api/Data/Repositories/VentasRepository.cs:22 | Nivel 3: bloque de comentario al inicio del metodo `ListarAsync` (lineas 22-23) aplica a la consulta armada con `StringBuilder` (lineas 24-38). La consulta no llama SPs: `'PCK_VENTAS.SP_IMPORTAR_VENTAS'` (linea 29) es un literal SQL y se ignora. |
| GET /api/Ventas/{id} | PCK_VENTAS.SP_OBTENER_VENTA | - | DIRECTO | src/Ventas.Api/Data/Repositories/VentasRepository.cs:52 | Constante `StoredProcedures.ObtenerVenta` definida en src/Ventas.Api/Data/StoredProcedures.cs:11. `QueryFirstOrDefaultAsync` + `OracleDynamicParameters` con `RefCursor` + `CommandType.StoredProcedure`. Sobrecarga de 1 argumento `ObtenerVentaAsync(int)` (parte 1 de la clase parcial). El `///` de VentasController.cs:28 nombra el mismo SP => solo documenta, no agrega fila. |
| GET /api/Ventas/{id}/detalle | PCK_VENTAS.SP_OBTENER_VENTA_DETALLE | - | DIRECTO | src/Ventas.Api/Data/Repositories/VentasRepository.Consultas.cs:22 | Interpolacion `$"{StoredProcedures.PaqueteVentas}.SP_OBTENER_VENTA_DETALLE"`; constante `PaqueteVentas` = "PCK_VENTAS" definida en src/Ventas.Api/Data/StoredProcedures.cs:9. Sobrecarga de 2 argumentos `ObtenerVentaAsync(int, bool)` en el OTRO archivo de la clase parcial (llamada con argumento nombrado `incluirDetalle: true`). Dos RefCursor + `QueryMultipleAsync`. |
| POST /api/Ventas | PCK_VENTAS.FN_SIGUIENTE_FOLIO | - | DIRECTO | src/Ventas.Api/Data/Repositories/VentasRepository.cs:59 | `SELECT PCK_VENTAS.FN_SIGUIENTE_FOLIO(:serie) FROM DUAL` (solo llamada a funcion desde DUAL). Alcanzado via VentasService.RegistrarAsync. |
| POST /api/Ventas | PCK_VENTAS.SP_REGISTRAR_VENTA | - | DIRECTO | src/Ventas.Api/Data/Repositories/VentasRepository.cs:64 | Wrapper `BaseRepository.ExecuteSpAsync(string spName, object param)` (src/Ventas.Api/Data/BaseRepository.cs:24, ejecuta con `CommandType.StoredProcedure` en la linea 27) recibiendo la constante `StoredProcedures.RegistrarVenta` definida en src/Ventas.Api/Data/StoredProcedures.cs:12. |
| PUT /api/Ventas/{id} | PCK_VENTAS.SP_ACTUALIZAR_VENTA | - | DIRECTO | src/Ventas.Api/Data/Repositories/VentasRepository.cs:78 | Bloque PL/SQL anonimo `BEGIN ... END;` en verbatim `@""` multilinea, compuesto solo por llamadas. |
| PUT /api/Ventas/{id} | PCK_AUDITORIA.SP_REGISTRAR_CAMBIO | - | DIRECTO | src/Ventas.Api/Data/Repositories/VentasRepository.cs:79 | Mismo bloque anonimo; el argumento `'VENTAS'` es un literal y no afecta. |
| DELETE /api/Ventas/{id} | PCK_VENTAS.SP_ANULAR_VENTA | - | DIRECTO | src/Ventas.Api/Data/Repositories/VentasRepository.cs:96 | String literal exacto + `CommandType.StoredProcedure`. El `LogError` de VentasController.cs:70 y el `throw new InvalidOperationException(...)` de VentasRepository.cs:102 nombran el mismo SP pero NO son llamadas (la linea correcta es 96). |
| ANY /api/Ventas/sincronizar | PCK_INTEGRACION.SP_SINCRONIZAR_VENTAS | - | DIRECTO | src/Ventas.Api/Data/Repositories/VentasRepository.cs:109 | `CALL PCK_INTEGRACION.SP_SINCRONIZAR_VENTAS(:pFecha)`. |
| GET /api/v2/ventas/resumen | PCK_VENTAS.FN_TOTAL_VENTA | - | DIRECTO_EN_QUERY | src/Ventas.Api/Data/Repositories/VentasRepository.Consultas.cs:39 | Funcion dentro de un SELECT con GROUP BY (consulta regular). No hay marcador en ningun nivel (sin comentarios con SP en SQL, metodo, servicio, accion ni clase) => no es migracion. |
| GET /api/Ventas/medios-pago | PCK_PARAMETROS.SP_LISTAR_MEDIOS_PAGO | - | MIGRADO_LISTO | src/Ventas.Api/Data/Repositories/ParametrosRepository.cs:8 | Nivel 6: comentario de clase de `ParametrosRepository` que nombra exactamente UN SP; la consulta (linea 20) no tiene marcadores en niveles 1-5 y no llama SPs. Alcanzado via VentasService -> `_parametrosRepository`. |
| GET /api/Clientes | PCK_CLIENTES.SP_LISTAR_CLIENTES | PCK_UTIL.FN_FORMATEAR_RUT | HIJO | src/Ventas.Api/Data/Repositories/ClientesRepository.cs:19 | Nivel 2: marcador encima del campo `const string SqlListarClientes` (ClientesRepository.cs:13). La consulta migrada llama 2 funciones => 2 filas HIJO. El literal `'/*'` de la linea 18 NO abre comentario. |
| GET /api/Clientes | PCK_CLIENTES.SP_LISTAR_CLIENTES | PCK_CLIENTES.FN_SALDO_CLIENTE | HIJO | src/Ventas.Api/Data/Repositories/ClientesRepository.cs:20 | Mismo marcador nivel 2 (linea 13). |
| GET /api/Clientes | PCK_CLIENTES.SP_LISTAR_CLIENTES | PCK_CLIENTES.FN_SALDO_CLIENTE -> PCK_COBRANZA.FN_TOTAL_PAGADO | HIJO_N2 | src/Ventas.Api/Data/Repositories/CobranzaRepository.cs:20 | El hijo FN_SALDO_CLIENTE esta migrado en OTRO repositorio (marcador nivel 1 en CobranzaRepository.cs:19); esa consulta llama a FN_TOTAL_PAGADO (linea 20), que se reporta como nieto del padre original. FN_FORMATEAR_RUT no esta migrado en ningun lugar incluido (el marcador de tests/ no cuenta). |
| GET /api/Clientes/{id} | PCK_CLIENTES.SP_OBTENER_CLIENTE | - | MIGRADO_LISTO | src/Ventas.Api/Data/Repositories/ClientesRepository.cs:37 | Nivel 3: comentario al final de la misma linea de la consulta (trailing comment). La consulta no llama SPs. |
| GET /api/Clientes/{id}/saldo | PCK_CLIENTES.FN_SALDO_CLIENTE | PCK_COBRANZA.FN_TOTAL_PAGADO | HIJO | src/Ventas.Api/Data/Repositories/CobranzaRepository.cs:20 | Nivel 1: comentario `/* Migrado de ... */` dentro del texto SQL (CobranzaRepository.cs:19). Alcanzado via ClientesService -> `_cobranzaRepository` (segundo repositorio inyectado). |
| POST /api/Clientes/{id}/pagos | PCK_COBRANZA.SP_REGISTRAR_PAGO | - | DIRECTO | src/Ventas.Api/Data/Repositories/CobranzaRepository.cs:34 | Bloque anonimo con identificadores entre comillas `"PCK_COBRANZA"."SP_REGISTRAR_PAGO"` dentro de un raw string literal C# 11 (`"""`). |
| GET /api/Clientes/buscar | PCK_UTIL.FN_NORMALIZAR | - | DIRECTO_EN_QUERY | src/Ventas.Api/Data/Repositories/ClientesRepository.cs:48 | Aparece 2 veces en la misma linea => 1 sola fila. El literal `'--'` previo en la misma linea NO es comentario (si se tratara como comentario se perderia la llamada). El comentario de clase (ClientesRepository.cs:9) nombra 2 SPs => nivel 6 no aplica, sigue siendo DIRECTO_EN_QUERY. |
| POST /api/Clientes/buscar | PCK_UTIL.FN_NORMALIZAR | - | DIRECTO_EN_QUERY | src/Ventas.Api/Data/Repositories/ClientesRepository.cs:48 | Mismo handler que GET /api/Clientes/buscar (dos atributos Http en la accion). |
| DELETE /api/Clientes | PCK_CLIENTES.SP_ELIMINAR_INACTIVOS | - | DIRECTO | src/Ventas.Api/Data/Repositories/ClientesRepository.cs:57 | Wrapper `ExecuteSpAsync` con string literal. El `<remarks>` de ClientesController.cs:56 y el `LogInformation` de ClientesService.cs:55 nombran el mismo SP => solo documentan. |
| GET /api/Productos | PCK_PRODUCTOS.SP_LISTAR_PRODUCTOS | - | MIGRADO_LISTO | src/Ventas.Api/Data/Repositories/ProductosRepository.cs:22 | Nivel 1: comentario `--` dentro del verbatim SQL. `'PCK_CATALOGO.SP_IMPORTAR'` (linea 26) es literal SQL => se ignora, la consulta no llama SPs. |
| GET /api/Productos/categorias | PCK_PRODUCTOS.SP_LISTAR_CATEGORIAS | - | MIGRADO_LISTO | src/Ventas.Api/Data/Repositories/ProductosRepository.cs:34 | Nivel 4: XML doc `/// <summary>` sobre el metodo del repositorio `ListarCategoriasAsync`; la consulta verbatim (lineas 40-43) no tiene comentarios y no llama SPs. |
| GET /api/Productos/PorCodigo/{codigo} | PCK_PRODUCTOS.SP_OBTENER_PRODUCTO | - | DIRECTO | src/Ventas.Api/Data/Repositories/ProductosRepository.cs:54 | Wrapper `QuerySpAsync<T>(string spName, OracleDynamicParameters)` (src/Ventas.Api/Data/BaseRepository.cs:31, `CommandType.StoredProcedure` en linea 34) con la constante `StoredProcedures.ObtenerProducto` definida en src/Ventas.Api/Data/StoredProcedures.cs:17. RefCursor. El `LogWarning` de ProductosService.cs:35 no es llamada. |
| GET /api/Productos/buscar | PCK_PRODUCTOS.SP_BUSCAR_PRODUCTOS | - | MIGRADO_LISTO | src/Ventas.Api/Controllers/ProductosController.cs:37 | Nivel 5 (heredado): `[SwaggerOperation(Description = "Package: PCK_PRODUCTOS - SP: SP_BUSCAR_PRODUCTOS")]` en la accion (forma separada paquete/SP => PCK_PRODUCTOS.SP_BUSCAR_PRODUCTOS). Se hereda a la consulta sin marcar de ProductosRepository.BuscarAsync (lineas 61-67; su comentario de la linea 60 no nombra SPs). |
| GET /api/Productos/{id}/precio | PCK_PRECIOS.FN_PRECIO_LISTA | - | DIRECTO | src/Ventas.Api/Data/Repositories/ProductosRepository.cs:78 | `SELECT PCK_PRECIOS.FN_PRECIO_LISTA@DBL_COMERCIAL(...) FROM DUAL`: db link, se reporta sin `@DBL_COMERCIAL`. |
| POST /api/Reportes/generar | PCK_REPORTES.SP_GENERAR_REPORTE | - | SOLO_COMENTARIO | src/Ventas.Api/Controllers/ReportesController.cs:18 | Marcador `///` en la accion. El repositorio ejecuta `conn.ExecuteAsync(spName, ...)` (ReportesRepository.cs:40) con `spName` leido de configuracion (linea 31): no resoluble y no hay SQL. appsettings.json no contiene el nombre. Requiere revision manual. |
| GET /api/Reportes/ventas-mensuales/{anio} | PCK_REPORTES.SP_VENTAS_MENSUALES | PCK_VENTAS.FN_TOTAL_VENTA | HIJO | src/Ventas.Api/Queries/Reportes/VentasMensuales.sql:5 | SQL cargado desde archivo referenciado por nombre: `File.ReadAllText(Path.Combine(..., "VentasMensuales.sql"))` en el campo `static readonly SqlVentasMensuales` (ReportesRepository.cs:14-15), usado en la linea 25. Marcador nivel 1 en VentasMensuales.sql:2. FN_TOTAL_VENTA no esta migrado en codigo incluido (la propuesta de docs/ no cuenta) => sin HIJO_N2. |

## Endpoints sin SP

- GET /api/Salud/Ping  (Handler: SaludController.Ping)
- GET /api/Salud/version  (Handler: SaludController.Version)

## Referencias huerfanas

Referencias a SP en codigo incluido pero no alcanzable desde ningun endpoint:

| Archivo:linea | SP | Motivo |
|---|---|---|
| src/Ventas.Api/Data/Repositories/VentasRepository.cs:115 | PCK_VENTAS.SP_RECALCULAR_TOTALES | `RecalcularTotalesAsync` solo se invoca desde `VentasService.RecalcularTotalesAsync` (VentasService.cs:68), que solo invoca `VentasController.RecalcularTotales` (VentasController.cs:100), marcado `[NonAction]` (linea 99) => no es endpoint. |
| src/Ventas.Api/Data/Repositories/ProductosRepository.cs:85 | PCK_PRODUCTOS.SP_DESCONTINUAR | `DescontinuarAsync` esta en `IProductosRepository` pero ningun servicio/controlador la llama. |
| src/Ventas.Api/Data/StoredProcedures.cs:15 | PCK_VENTAS.SP_CERRAR_CAJA | Constante `StoredProcedures.CerrarCaja` declarada pero nunca usada (referencia sin vinculo). |

## No debe aparecer

1. `tests/Ventas.Tests/**` (proyecto de pruebas xUnit + Moq) se excluye completo:
   - VentasRepositoryTests.cs:19-20 (`"PCK_VENTAS.SP_REGISTRAR_VENTA"`, `"PCK_VENTAS.SP_OBTENER_VENTA"` en `Assert.Equal`) y el comentario de la linea 26.
   - VentasRepositoryTests.cs:42-43: marcador `-- Migrado de PCK_UTIL.FN_FORMATEAR_RUT` con llamada a `PCK_UTIL.FN_LIMPIAR_TEXTO`. Si se incluyera, generaria un falso HIJO_N2 `PCK_UTIL.FN_FORMATEAR_RUT -> PCK_UTIL.FN_LIMPIAR_TEXTO` en GET /api/Clientes.
   - `PruebasController` (lineas 50-64): NO existe el endpoint `GET /api/pruebas/semilla`, ni `PCK_PRUEBAS.SP_CARGAR_SEMILLA` (linea 61).
2. `docs/analisis-migracion.md`: tabla con SPs, llamadas `PCK_VENTAS.SP_CERRAR_CAJA(...)`/`PCK_FACTURACION.SP_EMITIR_FACTURA(...)` (linea 16), bloque SQL `-- Migrado de PCK_VENTAS.FN_TOTAL_VENTA` + `PCK_IMPUESTOS.FN_IVA` (lineas 23-24; NO debe crear HIJO_N2 `PCK_VENTAS.FN_TOTAL_VENTA -> PCK_IMPUESTOS.FN_IVA` en GET /api/Reportes/ventas-mensuales/{anio}) y snippet C# `[HttpPost("~/api/facturas")]` (NO es endpoint).
3. `docs/sp/PCK_VENTAS.sql`: fuente del paquete, no referenciada desde C# (PCK_VENTAS.FN_TOTAL_VENTA, PCK_AUDITORIA.SP_REGISTRAR_CAMBIO, PCK_CAJA.SP_CUADRAR, PCK_IMPUESTOS.FN_IVA, etc.).
4. `src/Ventas.Api/bin/Debug/net8.0/Queries/Reportes/VentasMensuales.sql`: copia en bin/ del .sql referenciado. No debe generar filas duplicadas con ruta en bin/.
5. `src/Ventas.Api/Queries/Reportes/VentasMensuales_v1.sql`: .sql dentro de src/ pero NO referenciado por nombre desde C# (el nombre referenciado es exactamente `VentasMensuales.sql`). `PCK_REPORTES.FN_VENTAS_MES` no debe aparecer en ningun lado (ni como huerfana).
6. Mensajes de log / excepcion (no son llamadas; tampoco cambian la linea de la fila DIRECTO):
   - VentasController.cs:70 `_logger.LogError(..."PCK_VENTAS.SP_ANULAR_VENTA"...)`.
   - VentasRepository.cs:102 `throw new InvalidOperationException($"PCK_VENTAS.SP_ANULAR_VENTA ... {{id={id}}}")` (ademas llaves escapadas en string interpolado).
   - ClientesService.cs:55 `LogInformation("Ejecutando PCK_CLIENTES.SP_ELIMINAR_INACTIVOS ...")`.
   - ProductosService.cs:35 `LogWarning("PCK_PRODUCTOS.SP_OBTENER_PRODUCTO ...")`.
7. Literales SQL entre comillas simples:
   - VentasRepository.cs:29 `'PCK_VENTAS.SP_IMPORTAR_VENTAS'` => GET /api/Ventas sigue siendo MIGRADO_LISTO (no HIJO).
   - ProductosRepository.cs:26 `'PCK_CATALOGO.SP_IMPORTAR'` => GET /api/Productos sigue siendo MIGRADO_LISTO.
   - ClientesRepository.cs:18 `'/*'` no abre comentario (si lo hiciera, se perderian los 2 HIJO y el HIJO_N2).
   - ClientesRepository.cs:48 `'--'` no abre comentario (si lo hiciera, se perderia FN_NORMALIZAR).
8. Comentario de clase de ClientesRepository (lineas 7-10) nombra DOS SPs (`PCK_CLIENTES.SP_LISTAR_CLIENTES`, `PCK_CLIENTES.SP_OBTENER_CLIENTE`) => nivel 6 no aplica: no genera MIGRADO_LISTO/HIJO en GET/POST /api/Clientes/buscar ni filas SOLO_COMENTARIO.
9. Marcadores que coinciden con el SP realmente llamado solo documentan (sin MIGRADO_LISTO ni SOLO_COMENTARIO extra): VentasController.cs:28 (`///` de Obtener), ClientesController.cs:56 (`<remarks>`), y los logs del punto 6.
10. Sobrecargas: GET /api/Ventas/{id} NO incluye PCK_VENTAS.SP_OBTENER_VENTA_DETALLE y GET /api/Ventas/{id}/detalle NO incluye PCK_VENTAS.SP_OBTENER_VENTA (resolucion por cantidad de argumentos, sobrecargas en archivos distintos de la clase parcial).
11. Metodos homonimos en clases distintas (`ListarAsync`/`BuscarAsync`/`ObtenerAsync` existen en varios servicios y repositorios): la resolucion es por tipo del campo inyectado. Ej.: GET /api/Productos NO debe traer filas de VentasRepository.ListarAsync ni de ClientesRepository.ListarAsync.
12. `nameof(Obtener)` en VentasController.cs:49 no es una invocacion: POST /api/Ventas NO incluye PCK_VENTAS.SP_OBTENER_VENTA.
13. `[NonAction] RecalcularTotales` (VentasController.cs:99-104) no es endpoint (su SP es huerfano).
14. Strings con `//`, `/*` o llaves que no son comentarios ni rutas: ProductosRepository.cs:12-13 (`"/*"`, `"//"`, sin `*/` posterior en el archivo), SaludController.cs:23-25 (`"http://.../*"`, `"api/{controller}/{id?}"`, `"/* no es un comentario */ // tampoco esto"`), VentasController.cs:80 (`"Sincronizacion {ok}"`). Comentarios con corchetes/comillas/llaves: VentasController.cs:7 (`[Route("api/[controller]")]` dentro de un comentario, no es atributo) y BaseApiController.cs:13.
15. Constantes usadas (StoredProcedures.cs:9, 11, 12, 17) NO son huerfanas: se reportan en el punto de uso. `PCK_VENTAS` solo (StoredProcedures.cs:9) no es un nombre de SP.
16. Nombre suelto `SP_BUSCAR_PRODUCTOS` (sin paquete) no debe reportarse aparte: se combina con `Package: PCK_PRODUCTOS`.
17. `Program.cs`, `appsettings.json`, `.csproj`, `.sln`: sin SPs; `app.MapControllers()` no define endpoints propios.

## Escenarios cubiertos

- `[ApiController]` + `[Route("api/[controller]")]` en `BaseApiController` abstracto, heredado por 4 controladores derivados; un controlador (`SaludController`) con su propio `[Route]` sobre `ControllerBase`.
- `[HttpGet]`, `[HttpGet("{id:int}")]`, `[HttpGet("{id:int}/detalle")]`, `[HttpPost]`, `[HttpPut("{id}")]`, `[HttpDelete("{id:int}")]`, `[HttpDelete]` sin plantilla, `{anio:int?}` (restriccion + opcional).
- Accion con dos atributos Http (`[HttpGet("buscar")]` + `[HttpPost("buscar")]`) => 2 endpoints.
- `[ActionName("PorCodigo")]` con token `[action]`; `[action]` sin ActionName en metodo sin sufijo Async (`Ping`).
- Ruta absoluta `~/api/v2/ventas/resumen`.
- Accion sin atributo de verbo (`[Route("sincronizar")]`) => `ANY`.
- `[NonAction]` publico que alcanza un SP => no endpoint + referencia huerfana.
- Controller -> Service -> Repository con interfaces inyectadas por constructor (`_ventasService`, `_clientesRepository`, `_cobranzaRepository`, `_parametrosRepository`...); un servicio con dos repositorios.
- Metodo de clase base del controlador (`OkOrNotFound`) en el flujo (sin SQL).
- Dapper + Dapper.Oracle `OracleDynamicParameters` con `OracleMappingType.RefCursor` (1 y 2 cursores, `QueryMultipleAsync`).
- Wrappers `BaseRepository.ExecuteSpAsync(string spName, object param)` y `QuerySpAsync<T>(string spName, ...)` con `CommandType.StoredProcedure` (resolucion del argumento en el punto de llamada).
- Clase estatica `StoredProcedures` con `const` de nombres de SP; constante sin uso (huerfana); interpolacion `$"{StoredProcedures.PaqueteVentas}.SP_..."`.
- String literal exacto con `CommandType.StoredProcedure`.
- Bloque PL/SQL anonimo con 2 llamadas; bloque anonimo con identificadores entre comillas en raw string literal `"""`.
- `CALL PCK_X.SP_Y(...)`.
- `SELECT PCK_X.FN_Y(:p) FROM DUAL`, y variante con db link `@DBL_COMERCIAL`.
- DIRECTO_EN_QUERY: funcion dentro de SELECT con agregacion; funcion repetida en la misma linea (1 fila).
- Marcador nivel 1: `-- Migrado de` y `/* Migrado de */` dentro del texto SQL (C# y archivo .sql).
- Marcador nivel 2: bloque de comentarios encima de `private const string` SQL.
- Marcador nivel 3: bloque al inicio del metodo con `StringBuilder`; comentario al final de la misma linea.
- Marcador nivel 4: XML doc sobre el metodo del repositorio.
- Marcador nivel 5 (heredado): `[SwaggerOperation(Description = "Package: X - SP: Y")]` en la accion (forma separada paquete/SP).
- Marcador nivel 6: comentario de clase con exactamente 1 SP (aplica) y con 2 SPs (se ignora).
- MIGRADO_LISTO (6 casos, niveles 1-6 salvo 2), HIJO con 2 hijos, HIJO con 1 hijo, HIJO_N2 (hijo migrado en OTRO repositorio), SOLO_COMENTARIO (nombre de SP desde configuracion, no resoluble).
- Verbatim `@""` multilinea (varios), string regular de una linea, raw string literal, `StringBuilder`.
- .sql referenciado por nombre de archivo (`File.ReadAllText(Path.Combine(..., "VentasMensuales.sql"))` en campo `static readonly`) vs .sql no referenciado en src/, copia en bin/, .sql en docs/.
- Clase repositorio `partial` en 2 archivos (`VentasRepository.cs` + `VentasRepository.Consultas.cs`) con sobrecargas de distinta cantidad de argumentos repartidas entre ambos.
- Trampas: logs/excepciones con nombres de SP, literales SQL con nombres de SP, `'/*'` y `'--'` en literales SQL, `"/*"` y `"//"` en strings C#, llaves en strings interpolados (`{{id={id}}}`), comentario con atributo de ruta, `nameof(...)`, proyecto de tests con mocks y controlador falso, docs/ con .md y .sql, metodo inalcanzable.
