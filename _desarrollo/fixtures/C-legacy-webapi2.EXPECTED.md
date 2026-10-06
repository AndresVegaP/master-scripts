# EXPECTED - fixture `C-legacy-webapi2`

Legacy .NET Framework 4.8 / ASP.NET Web API 2 (`ApiController`), Oracle.ManagedDataAccess + Dapper/Dapper.Oracle.
Rutas: por atributo (`[RoutePrefix]` + `[Route]`) y convencionales (`config.Routes.MapHttpRoute("DefaultApi", "api/{controller}/{id}", ...)` en `src/Comercial.Api/App_Start/WebApiConfig.cs:14-18`).
Rutas de archivo relativas a la raíz del fixture. Todas las líneas son 1-based y fueron verificadas releyendo los archivos.

## Endpoints

Total: 26 endpoints.

| # | Endpoint | Handler |
|---|----------|---------|
| 1 | GET /api/clientes/{id}  | (Handler: ClientesController.Obtener) |
| 2 | GET /api/clientes/buscar  | (Handler: ClientesController.Buscar) |
| 3 | POST /api/clientes  | (Handler: ClientesController.Crear) |
| 4 | PUT /api/clientes/{id}  | (Handler: ClientesController.Actualizar) |
| 5 | DELETE /api/clientes/{id}  | (Handler: ClientesController.Eliminar) |
| 6 | GET /api/clientes/{id}/resumen  | (Handler: ClientesController.GetResumen) |
| 7 | GET /api/Productos  | (Handler: ProductosController.GetAll) |
| 8 | GET /api/Productos/{id}  | (Handler: ProductosController.GetById) |
| 9 | POST /api/Productos  | (Handler: ProductosController.Post) |
| 10 | PUT /api/Productos/{id}  | (Handler: ProductosController.Put) |
| 11 | DELETE /api/Productos/{id}  | (Handler: ProductosController.Delete) |
| 12 | GET /api/ventas/cliente/{idCliente}  | (Handler: VentasController.ListarPorCliente) |
| 13 | GET /api/ventas/{id}  | (Handler: VentasController.Obtener) |
| 14 | GET /api/ventas/pendientes  | (Handler: VentasController.ContarPendientes) |
| 15 | POST /api/ventas  | (Handler: VentasController.Registrar) |
| 16 | POST /api/ventas/{id}/anular  | (Handler: VentasController.Anular) |
| 17 | POST /api/ventas/reprocesar  | (Handler: VentasController.Reprocesar) |
| 18 | GET /api/reportes/ventas-mensuales/{anio}/{mes}  | (Handler: ReportesController.VentasMensuales) |
| 19 | GET /api/reportes/top-clientes  | (Handler: ReportesController.TopClientes) |
| 20 | GET /api/reportes/historico/{idCliente}  | (Handler: ReportesController.Historico) |
| 21 | GET /api/reportes/totales/{idCliente}  | (Handler: ReportesController.GetTotales) |
| 22 | POST /api/reportes/archivar/{anio}  | (Handler: ReportesController.Archivar) |
| 23 | DELETE /api/reportes/cache  | (Handler: ReportesController.DeleteCache) |
| 24 | GET /api/precios/{idProducto}  | (Handler: PreciosController.Calcular) |
| 25 | GET /api/precios/segmento/{codigo}  | (Handler: PreciosController.PorSegmento) |
| 26 | GET /api/salud  | (Handler: SaludController.Ping) |

Formato plano (una por línea):

```
GET /api/clientes/{id}  (Handler: ClientesController.Obtener)
GET /api/clientes/buscar  (Handler: ClientesController.Buscar)
POST /api/clientes  (Handler: ClientesController.Crear)
PUT /api/clientes/{id}  (Handler: ClientesController.Actualizar)
DELETE /api/clientes/{id}  (Handler: ClientesController.Eliminar)
GET /api/clientes/{id}/resumen  (Handler: ClientesController.GetResumen)
GET /api/Productos  (Handler: ProductosController.GetAll)
GET /api/Productos/{id}  (Handler: ProductosController.GetById)
POST /api/Productos  (Handler: ProductosController.Post)
PUT /api/Productos/{id}  (Handler: ProductosController.Put)
DELETE /api/Productos/{id}  (Handler: ProductosController.Delete)
GET /api/ventas/cliente/{idCliente}  (Handler: VentasController.ListarPorCliente)
GET /api/ventas/{id}  (Handler: VentasController.Obtener)
GET /api/ventas/pendientes  (Handler: VentasController.ContarPendientes)
POST /api/ventas  (Handler: VentasController.Registrar)
POST /api/ventas/{id}/anular  (Handler: VentasController.Anular)
POST /api/ventas/reprocesar  (Handler: VentasController.Reprocesar)
GET /api/reportes/ventas-mensuales/{anio}/{mes}  (Handler: ReportesController.VentasMensuales)
GET /api/reportes/top-clientes  (Handler: ReportesController.TopClientes)
GET /api/reportes/historico/{idCliente}  (Handler: ReportesController.Historico)
GET /api/reportes/totales/{idCliente}  (Handler: ReportesController.GetTotales)
POST /api/reportes/archivar/{anio}  (Handler: ReportesController.Archivar)
DELETE /api/reportes/cache  (Handler: ReportesController.DeleteCache)
GET /api/precios/{idProducto}  (Handler: PreciosController.Calcular)
GET /api/precios/segmento/{codigo}  (Handler: PreciosController.PorSegmento)
GET /api/salud  (Handler: SaludController.Ping)
```

Notas de ruteo:
- `ClientesController.cs:15` `{id:int}` -> `{id}`; `ReportesController.cs:12` `{anio:int:min(2000)}/{mes:int?}` -> `{anio}/{mes}` (restricción encadenada con paréntesis + opcional); `PreciosController.cs:14` `{codigo?}` -> `{codigo}`.
- `[Route("")]` + `[RoutePrefix("api/clientes")]` -> `/api/clientes` (sin barra final). Igual para `/api/ventas` y `/api/salud`.
- Verbo inferido por prefijo del nombre (Web API 2) cuando no hay atributo de verbo: `ClientesController.GetResumen` (GET, línea 61), `ReportesController.GetTotales` (GET, línea 30), `ReportesController.DeleteCache` (DELETE, línea 42) y todas las acciones de `ProductosController` (GetAll/GetById -> GET, Post -> POST, Put -> PUT, Delete -> DELETE).
- `ProductosController` no tiene atributos de ruta: usa la ruta convencional `api/{controller}/{id}` con `id = RouteParameter.Optional`. `{controller}` -> `Productos` (nombre de clase sin sufijo, se respeta la mayúscula). El segmento opcional `{id}` se incluye solo si la acción tiene un parámetro llamado `id` (comportamiento de ApiExplorer/Swashbuckle): `GetAll` y `Post` -> `/api/Productos`; `GetById`, `Put`, `Delete` -> `/api/Productos/{id}`.
- La ruta convencional NO genera endpoints extra para los controladores con rutas por atributo (Clientes, Ventas, Reportes, Precios, Salud).

## Filas esperadas

Total: 29 filas. DIRECTO 14, DIRECTO_EN_QUERY 2, MIGRADO_LISTO 6, HIJO 5, HIJO_N2 1, SOLO_COMENTARIO 1.

| Endpoint | SP | Hijo | Tipo | Archivo:linea | Notas |
|----------|----|------|------|---------------|-------|
| GET /api/clientes/{id} | PCK_CLIENTES.SP_OBTENER_CLIENTE | | DIRECTO | src/Comercial.Data/Dal/ClientesDal.cs:29 | DAL estático + OracleCommand: `cmd.CommandType = CommandType.StoredProcedure` (línea 28) y `cmd.CommandText = "PCK_CLIENTES.SP_OBTENER_CLIENTE"`. El `#region SP: PCK_CLIENTES.SP_OBTENER_CLIENTE` (línea 22) y el `Log.WarnFormat(...)` (línea 38) nombran el mismo SP: no generan filas ni cambian la línea. |
| GET /api/clientes/buscar | PCK_CLIENTES.SP_BUSCAR | | DIRECTO | src/Comercial.Data/Dal/ClientesDal.cs:64 | Bloque anónimo en minúsculas `begin pck_clientes.sp_buscar(:p_texto, :p_cursor); end;` -> se reporta en MAYÚSCULAS. Dapper `Query<Cliente>`. |
| POST /api/clientes | PCK_CLIENTES.SP_INSERTAR | | DIRECTO | src/Comercial.Data/Dal/ClientesDal.cs:76 | Identificadores con comillas escapadas en string C# normal: `BEGIN \"PCK_CLIENTES\".\"SP_INSERTAR\"(...); END;` (CommandType.Text, bloque solo con la llamada). El `throw new DataException("PCK_CLIENTES.SP_INSERTAR ...")` (línea 85) no es llamada. |
| PUT /api/clientes/{id} | PCK_CLIENTES.SP_ACTUALIZAR | | MIGRADO_LISTO | src/Comercial.Data/Dal/ClientesDal.cs:93 | Marcador nivel 3 (bloque de comentarios dentro del método, líneas 93-94) que nombra paquete y SP por separado: `// Package: PCK_CLIENTES / SP: SP_ACTUALIZAR` -> PCK_CLIENTES.SP_ACTUALIZAR. Consulta asociada: UPDATE CLIENTES (líneas 98-102) sin llamadas a SP. NO debe salir un SP_ACTUALIZAR standalone. |
| DELETE /api/clientes/{id} | PCK_CLIENTES.SP_ELIMINAR | | DIRECTO | src/Comercial.Data/Dal/ClientesDal.cs:112 | Dapper `Execute("PCK_CLIENTES.SP_ELIMINAR", ..., commandType: CommandType.StoredProcedure)` (commandType en línea 114). El `#region PCK_CLIENTES.SP_ELIMINAR` (línea 107) no es fila. |
| DELETE /api/clientes/{id} | PCK_AUDITORIA.SP_REGISTRAR_ACCESO | | DIRECTO | src/Comercial.Api/Controllers/BaseApiController.cs:17 | Vía clase base: `ClientesController.Eliminar` (ClientesController.cs:55) llama `BaseApiController.RegistrarAcceso`, que pasa `$"{Paquetes.Auditoria}.SP_REGISTRAR_ACCESO"` al wrapper `OracleHelper.EjecutarSp(string nombreSp, ...)` (StoredProcedure en src/Comercial.Data/Infraestructura/OracleHelper.cs:18). Const `Paquetes.Auditoria = "PCK_AUDITORIA"` definida en src/Comercial.Data/Dal/Paquetes.cs:8. |
| GET /api/clientes/{id}/resumen | PCK_CLIENTES.FN_SALDO_CLIENTE | | DIRECTO | src/Comercial.Data/Dal/ClientesDal.cs:127 | `SELECT fn(...) AS x, fn(...) AS y FROM DUAL` (solo llamadas a funciones desde DUAL; los alias no cambian el tipo). Método genérico `ObtenerResumen<T>`; acción expression-bodied sin atributo de verbo. |
| GET /api/clientes/{id}/resumen | PCK_CLIENTES.FN_CATEGORIA | | DIRECTO | src/Comercial.Data/Dal/ClientesDal.cs:128 | Mismo SELECT ... FROM DUAL que la fila anterior (segunda función, otra línea). |
| GET /api/Productos | PCK_PRODUCTOS.SP_LISTAR | | DIRECTO | src/Comercial.Data/Dal/ProductosDal.cs:20 | Nombre calificado con esquema `VENTAS_OWN.PCK_PRODUCTOS.SP_LISTAR` (el esquema se descarta) + `commandType: CommandType.StoredProcedure`. Ruta convencional. |
| GET /api/Productos/{id} | PCK_PRODUCTOS.FN_PRECIO_VIGENTE | | DIRECTO_EN_QUERY | src/Comercial.Data/Dal/ProductosDal.cs:31 | SELECT ... FROM PRODUCTOS sin marcador en ningún nivel (el comentario de clase de ProductosDal, líneas 10-11, solo nombra el paquete PCK_PRODUCTOS sin miembro; la acción solo tiene `// GET api/Productos/5`). |
| POST /api/Productos | PCK_PRODUCTOS.SP_INSERTAR | | DIRECTO | src/Comercial.Data/Dal/ProductosDal.cs:38 | Línea de uso de la const `Paquetes.PRODUCTOS_INSERTAR` (definida en src/Comercial.Data/Dal/Paquetes.cs:11 = "PCK_PRODUCTOS.SP_INSERTAR") pasada al wrapper `OracleHelper.EjecutarSp` (StoredProcedure, OracleHelper.cs:18). Miembro expression-bodied. |
| PUT /api/Productos/{id} | PCK_PRODUCTOS.SP_ACTUALIZAR | PCK_UTIL.FN_NORMALIZAR_TEXTO | HIJO | src/Comercial.Data/Dal/ProductosDal.cs:48 | Marcador nivel 1: comentario SQL `-- Migrado de PCK_PRODUCTOS.SP_ACTUALIZAR` dentro del MERGE (línea 45). La línea es la del hijo. |
| PUT /api/Productos/{id} | PCK_PRODUCTOS.SP_ACTUALIZAR | PCK_PRECIOS.FN_CALCULAR_PRECIO | HIJO | src/Comercial.Data/Dal/ProductosDal.cs:49 | Mismo MERGE/marcador (línea 45). |
| PUT /api/Productos/{id} | PCK_PRODUCTOS.SP_ACTUALIZAR | PCK_PRECIOS.FN_CALCULAR_PRECIO -> PCK_IMPUESTOS.FN_IVA | HIJO_N2 | src/Comercial.Data/Dal/PreciosDal.cs:23 | El hijo FN_CALCULAR_PRECIO está migrado en otra consulta regular alcanzable (PreciosDal.Calcular, marcador `/* Migrado de PCK_PRECIOS.FN_CALCULAR_PRECIO */` en PreciosDal.cs:20) que llama a PCK_IMPUESTOS.FN_IVA. Línea = aparición del nieto. (Ambigüedad: si el script ancla HIJO_N2 en la llamada al hijo, sería ProductosDal.cs:49.) |
| GET /api/ventas/cliente/{idCliente} | PCK_VENTAS.SP_LISTAR_POR_CLIENTE | PCK_UTIL.FN_FORMATEAR_RUT | HIJO | src/Comercial.Data/Queries/Ventas/ListarVentasPorCliente.sql:6 | .sql embebido referenciado por nombre en src/Comercial.Data/Repositorios/VentasRepositorio.cs:16 (`RecursosSql.Leer("Comercial.Data.Queries.Ventas.ListarVentasPorCliente.sql")`). Marcador nivel 1 en la línea 1 del .sql. Flujo vía interfaz `IVentasRepositorio` -> `VentasRepositorio` (única implementación fuera de tests). |
| GET /api/ventas/{id} | PCK_VENTAS.SP_OBTENER_VENTA | | MIGRADO_LISTO | src/Comercial.Data/Repositorios/VentasRepositorio.cs:24 | Marcador nivel 4: XML doc `/// Reemplazo de PCK_VENTAS.SP_OBTENER_VENTA` del método del repositorio. `v.SP_ORIGEN` (línea 34) es una columna (no va seguida de `(` ni `;`): no es llamada, la consulta no llama SPs. |
| GET /api/ventas/pendientes | PCK_VENTAS.FN_CONTAR_PENDIENTES | | MIGRADO_LISTO | src/Comercial.Data/Repositorios/VentasRepositorio.cs:97 | Marcador nivel 3 como comentario al final de la misma línea del SQL: `...ExecuteScalar<int>("SELECT COUNT(*) ...'P'"); // ex PCK_VENTAS.FN_CONTAR_PENDIENTES`. |
| POST /api/ventas | PCK_VENTAS.SP_REGISTRAR_CABECERA | | DIRECTO | src/Comercial.Data/Repositorios/VentasRepositorio.cs:56 | Bloque anónimo `BEGIN PCK_VENTAS.SP_REGISTRAR_CABECERA(...); END;` dentro de transacción. |
| POST /api/ventas | PCK_VENTAS.SP_REGISTRAR_DETALLE | | DIRECTO | src/Comercial.Data/Repositorios/VentasRepositorio.cs:62 | Nombre pelado + `commandType: CommandType.StoredProcedure` (línea 65), dentro de foreach. |
| POST /api/ventas | PRC_RECALCULAR_TOTALES | | DIRECTO | src/Comercial.Data/Repositorios/VentasRepositorio.cs:69 | Procedimiento standalone (sin paquete) en bloque anónimo, seguido de `(`. |
| POST /api/ventas/{id}/anular | PCK_VENTAS.SP_ANULAR_VENTA | | MIGRADO_LISTO | src/Comercial.Api/Controllers/VentasController.cs:49 | Marcador heredado (nivel 5): string del atributo `[Description("Origen: PCK_VENTAS.SP_ANULAR_VENTA")]` de la acción (nivel 4 del controlador) aplica al UPDATE VENTAS sin marcar de `VentasRepositorio.Anular` (VentasRepositorio.cs:88), que no llama SPs (`'A'` es literal). |
| POST /api/ventas/reprocesar | PCK_VENTAS.SP_REPROCESAR | | SOLO_COMENTARIO | src/Comercial.Api/Controllers/VentasController.cs:57 | Marcador en el `///` de la acción. El acceso a datos usa un nombre de SP variable: `ConfigurationManager.AppSettings["Ventas.SpReproceso"]` (VentasRepositorio.cs:104) -> `OracleHelper.EjecutarSp(nombreSp, ...)` (VentasRepositorio.cs:105): no resoluble, sin evidencia SQL. Revisión manual. |
| GET /api/reportes/ventas-mensuales/{anio}/{mes} | PCK_REPORTES.SP_VENTAS_MENSUALES | PCK_UTIL.FN_NOMBRE_MES | HIJO | src/Comercial.Data/Dal/ReportesDal.cs:22 | Marcador nivel 2: bloque de comentario sobre la const `ReportesDal.Sql.VentasMensuales` (línea 17; la línea 18 tiene llaves y comillas). Clase anidada + método genérico `Consultar<T>` que recibe la const como argumento (ReportesDal.cs:52). |
| GET /api/reportes/top-clientes | PCK_REPORTES.SP_TOP_CLIENTES | | MIGRADO_LISTO | src/Comercial.Data/Dal/ReportesDal.cs:29 | Marcador nivel 2: `/// <summary>Reemplaza PCK_REPORTES.SP_TOP_CLIENTES</summary>` sobre la const `TopClientes`. La línea 35 contiene un literal q-quote de Oracle `q'[... l'equipo usaba PCK_FAKE.SP_FAKE(1); ...]'` (con apóstrofo interno): es literal, se ignora -> la consulta no llama SPs. |
| GET /api/reportes/historico/{idCliente} | PCK_HISTORICO.SP_VENTAS_HIST | | DIRECTO | src/Comercial.Data/Dal/HistoricoDal.cs:23 | `cmd.CommandText = "PCK_HISTORICO.SP_VENTAS_HIST@DBLINK_HIST"` con CommandType.StoredProcedure (línea 22). El db link se descarta. |
| GET /api/reportes/totales/{idCliente} | PCK_VENTAS.FN_TOTAL_CLIENTE | | DIRECTO_EN_QUERY | src/Comercial.Data/Dal/HistoricoDal.cs:52 | `VENTAS_OWN.PCK_VENTAS.FN_TOTAL_CLIENTE(...)` (esquema descartado) dentro de un `$@"..."` interpolado con `{{`, `}}`, `""` y `{Esquema}`. Antes del SQL hay comentarios con `"`, `{` y `*/` (línea 45), strings con `//` (46) y `/*` (47) y char literals `'"'` (48) y `'{'` (49). El comentario de clase (línea 11) nombra DOS SPs -> no aplica nivel 6 -> sin marcador. |
| POST /api/reportes/archivar/{anio} | PCK_HISTORICO.SP_ARCHIVAR | | DIRECTO | src/Comercial.Data/Dal/HistoricoDal.cs:76 | String verbatim con comillas dobladas: `@"BEGIN ""PCK_HISTORICO"".""SP_ARCHIVAR""(:p_anio); END;"`. El `Console.WriteLine` (línea 73) no es llamada. |
| GET /api/precios/{idProducto} | PCK_PRECIOS.FN_CALCULAR_PRECIO | PCK_IMPUESTOS.FN_IVA | HIJO | src/Comercial.Data/Dal/PreciosDal.cs:23 | Marcador nivel 1 `/* Migrado de PCK_PRECIOS.FN_CALCULAR_PRECIO */` (línea 20) gana sobre la mención en `Log.Debug` (línea 16, nivel 4) y sobre el comentario de clase (línea 9, nivel 6). |
| GET /api/precios/segmento/{codigo} | PCK_PRECIOS.SP_LISTA_SEGMENTO | | MIGRADO_LISTO | src/Comercial.Data/Dal/PreciosDal.cs:9 | Marcador nivel 6: comentario de la clase PreciosDal que nombra exactamente UN SP; aplica a la consulta sin marcar de `PorSegmento` (línea 35, NVL es builtin, sin SPs). Ni la acción ni el método tienen marcadores. |

## Endpoints sin SP

- `DELETE /api/Productos/{id}  (Handler: ProductosController.Delete)` - `DELETE FROM PRODUCTOS ...` (ProductosDal.cs:63) sin marcadores; el comentario `// TODO: validar "dependencias" { ventas, stock }` (ProductosController.cs:44) no nombra SPs.
- `DELETE /api/reportes/cache  (Handler: ReportesController.DeleteCache)` - solo limpia MemoryCache, no hay acceso a datos.
- `GET /api/salud  (Handler: SaludController.Ping)` - `OracleHelper.Ping()` ejecuta `SELECT 'PCK_SALUD.SP_PING() ok' AS ECO FROM DUAL` (OracleHelper.cs:27): el nombre está dentro de un literal SQL `'...'` -> ignorado.

## Referencias huerfanas

| Archivo:linea | SP | Motivo |
|---------------|----|--------|
| src/Comercial.Data/Dal/ProductosDal.cs:75 | PCK_STOCK.SP_VALIDAR | `ProductosDal.ValidarStock` solo se llama desde `ProductosController.ValidarStock`, marcado `[NonAction]` (ProductosController.cs:50-51) y que ninguna acción invoca. |
| src/Comercial.Data/Dal/Legacy/ClientesLegacyDal.cs:22 | PCK_CLIENTES_OLD.SP_OBTENER | Clase `[Obsolete]` nunca referenciada desde la API. |
| src/Comercial.Data/Dal/Legacy/ClientesLegacyDal.cs:34 | PKG_MANTENCION.PRC_DEPURAR_LOG | Misma clase no alcanzable; prefijo de paquete `PKG_` y miembro `PRC_`. |
| src/Comercial.Data/Dal/Paquetes.cs:14 | PCK_PRODUCTOS.SP_DESCONTINUAR | Const `Paquetes.PRODUCTOS_DESCONTINUAR` definida pero nunca usada. (Las consts usadas, Paquetes.cs:8 y :11, NO son huérfanas.) |

## No debe aparecer

- **Menciones en log/excepción/consola** (no son llamadas; nombran el mismo SP que el método ya llama, así que tampoco generan filas extra ni cambian la línea): `Log.WarnFormat` ClientesDal.cs:38, `throw new DataException` ClientesDal.cs:85, `Console.WriteLine` HistoricoDal.cs:73, `Log.Debug` PreciosDal.cs:16.
- **Textos de `#region`** (ClientesDal.cs:22 y :107): no son llamadas ni filas. Nombran exactamente el SP llamado dentro de la región, por lo que tratarlos o no como marcador da el mismo resultado.
- **`SP_ACTUALIZAR` standalone** (ClientesDal.cs:93): se combina con `Package: PCK_CLIENTES` -> solo PCK_CLIENTES.SP_ACTUALIZAR.
- **`SP_REGISTRAR_ACCESO` standalone** (BaseApiController.cs:17): el `$"{Paquetes.Auditoria}.SP_REGISTRAR_ACCESO"` debe resolverse a PCK_AUDITORIA.SP_REGISTRAR_ACCESO, no reportarse sin paquete.
- **`PCK_SALUD.SP_PING`** (OracleHelper.cs:27): dentro de literal SQL `'...'`.
- **`PCK_FAKE.SP_FAKE`** (ReportesDal.cs:35): dentro de un literal q-quote `q'[...]'` con apóstrofo interno; un parser de comillas simples ingenuo lo vería fuera del literal (HIJO falso) y además se "comería" el resto del SQL.
- **`SP_ORIGEN`** (VentasRepositorio.cs:34): columna, no llamada (no va seguida de `(`/`;`, ni tras CALL/EXEC). GET /api/ventas/{id} sigue siendo MIGRADO_LISTO, no HIJO.
- **Comentario de clase de HistoricoDal** (HistoricoDal.cs:11): nombra 2 SPs -> no es marcador de clase; no debe producir MIGRADO_LISTO/SOLO_COMENTARIO para PCK_HISTORICO.SP_VENTAS_HIST / PCK_HISTORICO.SP_ARCHIVAR en `GET /api/reportes/totales/{idCliente}`, ni convertir su FN_TOTAL_CLIENTE en HIJO.
- **Strings/comentarios trampa** en HistoricoDal.cs:45-49 (`*/` en comentario de línea, `"http://...//..."`, `"/* no es comentario"`, `'"'`, `'{'`) y comentarios con llaves/comillas en ClientesController.cs:30, ProductosController.cs:9 y :44, ProductosDal.cs:11, ReportesDal.cs:18: no deben romper el tokenizado (FN_TOTAL_CLIENTE debe detectarse igual) ni generar filas.
- **`Created($"api/ventas/{id}", ...)`** (VentasController.cs:44) y `Created("api/clientes/" + id, ...)` (ClientesController.cs:39): no son rutas.
- **`ProductosController.ValidarStock`** (`[NonAction]`, ProductosController.cs:50-51): no es endpoint (sin `[NonAction]` Web API lo publicaría como `POST /api/Productos` duplicado). Su SP solo va a huérfanos.
- **`BaseApiController.RegistrarAcceso`** (protected, clase abstracta): no es endpoint.
- **Proyecto de pruebas** `tests/Comercial.Tests/` (MSTest + Moq, excluido completo):
  - mocks con `PCK_CLIENTES.SP_OBTENER_CLIENTE` (líneas 27, 29) y `PCK_TEST.SP_PREPARAR_DATOS` (32);
  - `VentasRepositorioFake : IVentasRepositorio` (líneas 37-53, con `PCK_VENTAS.SP_FAKE_OBTENER` y `PCK_VENTAS.SP_REPROCESAR_TEST`): NO debe tomarse como implementación de la interfaz; en particular `POST /api/ventas/reprocesar` debe seguir siendo SOLO_COMENTARIO;
  - `FakeClientesController` con `[RoutePrefix("api/fake")]`: no existe `GET /api/fake/{id}`; `PCK_FAKE_TEST.SP_OBTENER` no aparece;
  - const `SqlNombreMesFake` (líneas 18-21) con `-- Migrado de PCK_UTIL.FN_NOMBRE_MES` que llama `PCK_TEST.FN_MES_FAKE`: NO debe crear un HIJO_N2 `PCK_UTIL.FN_NOMBRE_MES -> PCK_TEST.FN_MES_FAKE` para `GET /api/reportes/ventas-mensuales/{anio}/{mes}`.
- **docs/**: `docs/MIGRACION.md` (PCK_DOCS.SP_SOLO_DOCUMENTADO, PCK_DOCS.SP_EJEMPLO, etc.) y `docs/sql/PCK_CLIENTES.pkb.sql` (cuerpo del paquete; si se leyera haría parecer que SP_ACTUALIZAR tiene hijo PCK_AUDITORIA.SP_REGISTRAR_ACCESO o que SP_OBTENER_CLIENTE llama FN_FORMATEAR_RUT).
- **`db/scripts/dependencias_ventas.sql`**: script de BD no referenciado desde C# (EXEC PCK_VENTAS.SP_RECOMPILAR_DEPENDENCIAS, PCK_DB_SCRIPT.SP_SOLO_SCRIPT). Ni filas ni huérfanos.
- **`src/Comercial.Data/Queries/Ventas/ListarVentasPorCliente_v1.sql`**: .sql no referenciado (nombre parecido al embebido; `PCK_VENTAS.SP_LISTAR_CLIENTE_V1`). Ni filas ni huérfanos.
- **`src/Comercial.Api/bin/Queries/Ventas/ListarVentasPorCliente.sql`**: copia en bin con el MISMO nombre de archivo que el recurso embebido; no debe resolverse a esta copia (`PCK_BIN.FN_COPIA_OBSOLETA` no aparece).
- **`src/Comercial.Api/obj/Debug/TemporaryGeneratedFile_SpCatalogo.cs`**: generado en obj (`PCK_OBJ.SP_GENERADO`, `PCK_OBJ.SP_GENERADO_2`).
- **`NOTAS.txt`**: .txt excluido (`PCK_NOTAS.SP_SOLO_TXT`, `CALL PCK_VENTAS.SP_REPROCESAR_NOCHE()`).
- **Paquete sin miembro**: `Paquetes.Productos = "PCK_PRODUCTOS"` (Paquetes.cs:9), `PCK_PRODUCTOS` en el comentario de ProductosDal.cs:10: no son nombres de SP.

## Escenarios cubiertos

- Web API 2 con `[RoutePrefix]` + `[Route]` + `[HttpGet/Post/Put/Delete]`, restricciones `{id:int}`, `{anio:int:min(2000)}`, opcionales `{mes:int?}` / `{codigo?}`, `[Route("")]`.
- Acciones sin atributo de verbo en controlador con rutas por atributo: verbo inferido por prefijo (`GetResumen`, `GetTotales`, `DeleteCache`).
- Controlador sin atributos con ruta convencional `api/{controller}/{id}` (`MapHttpRoute` en App_Start/WebApiConfig.cs): `GetAll`, `GetById(int id)`, `Post`, `Put`, `Delete`; `[NonAction]` público excluido.
- Controlador que hereda de una base abstracta con método protegido que llama un SP (`BaseApiController`).
- Interfaz -> implementación (`IVentasRepositorio` -> `VentasRepositorio`) con constructor por defecto sin IoC; fake de la interfaz en proyecto de tests que debe ignorarse.
- DAL estáticos con `OracleCommand` (`CommandType.StoredProcedure` + `CommandText = "PCK_CLIENTES.SP_OBTENER_CLIENTE"`) y DALs con Dapper / Dapper.Oracle (`OracleDynamicParameters`).
- Wrapper `OracleHelper.EjecutarSp(string nombreSp, ...)` con: const de clase de constantes, nombre interpolado `$"{Paquetes.Auditoria}.SP_..."`, y nombre NO resoluble (AppSettings) -> SOLO_COMENTARIO.
- Bloques anónimos `BEGIN ... END;` (mayúsculas y minúsculas), procedimiento standalone `PRC_`, `SELECT FN(...) AS x FROM DUAL` con dos funciones.
- Identificadores Oracle con comillas: `\"PCK_X\".\"SP_Y\"` (string normal) y `""PCK_X"".""SP_Y""` (verbatim).
- Nombres en minúsculas, calificados con esquema (`VENTAS_OWN.PCK_X.SP_Y` / `VENTAS_OWN.PCK_X.FN_Y`), db link (`@DBLINK_HIST`), prefijo `PKG_`.
- Marcadores nivel 1 (`--` y `/* */` dentro del SQL, también en .sql embebido), nivel 2 (`//` y `///` sobre const en clase anidada), nivel 3 (bloque en el cuerpo y comentario al final de la misma línea; "Package: X / SP: Y"), nivel 4 (XML doc del método), nivel 5 (string de atributo `[Description]` heredado del controlador), nivel 6 (comentario de clase con exactamente un SP) y comentario de clase con dos SPs (ignorado).
- Precedencia "closest wins": SQL comment (nivel 1) sobre Log (nivel 4) y comentario de clase (nivel 6) en PreciosDal.
- MIGRADO_LISTO, HIJO (en C# y en .sql embebido), HIJO_N2 (hijo migrado en otra consulta alcanzable), DIRECTO_EN_QUERY, SOLO_COMENTARIO.
- .sql embebido referenciado por nombre de recurso (`Comercial.Data.Queries.Ventas.ListarVentasPorCliente.sql`) vs .sql no referenciados (variante _v1, scripts en db/, copia en bin/).
- `#region` con nombres de SP, `$@""` interpolado verbatim con `{{ }}` y `""`, char literals `'"'` y `'{'`, strings con `//`, `/*`, `{`, comentarios con llaves, comillas y `*/`.
- Clases anidadas (`ReportesDal.Sql`), métodos genéricos (`ObtenerResumen<T>`, `Consultar<T>`), miembros expression-bodied (acciones y métodos DAL).
- Literales trampa: `'...'` SQL, q-quote `q'[...]'` con apóstrofo interno, columna `SP_ORIGEN` no llamada, menciones en log/excepción/Console.
- Exclusiones: proyecto `*.Tests` (MSTest/Moq, controlador fake, fake de interfaz, SQL con marcador), `docs/` (.md y .sql), `.txt`, `bin/`, `obj/`, scripts .sql no referenciados.
- Código no alcanzable -> huérfanos: método `[NonAction]`, clase legacy `[Obsolete]` sin referencias, const sin uso.

## Ambigüedades / decisiones documentadas

1. Ruta convencional con `{id}` opcional: se incluye `{id}` solo si la acción tiene parámetro `id` (como ApiExplorer). `GetAll`/`Post` -> `/api/Productos`.
2. Línea de HIJO = línea del nombre del hijo; línea de HIJO_N2 = línea del nieto en la consulta donde se migró el hijo (alternativa aceptable: línea de la llamada al hijo).
3. `#region` no se trata como marcador ni como llamada; el fixture está construido para que ambas interpretaciones den el mismo resultado.
4. Const de constantes nunca usada (Paquetes.cs:14) se lista como huérfana.
5. Los .sql no referenciados desde C# se excluyen por completo (no son filas ni huérfanos).
6. Menciones en log/excepción solo aparecen en métodos cuyo SQL llama ese mismo SP o tiene un marcador más cercano del mismo SP, para no depender de cómo se ponderan las menciones "débiles".
7. Aliases (`AS SALDO`) en un `SELECT ... FROM DUAL` de solo funciones no impiden la clasificación DIRECTO.
