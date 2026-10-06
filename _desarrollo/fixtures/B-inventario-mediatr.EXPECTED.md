# Ground truth - fixture B-inventario-mediatr

Clean Architecture .NET 8 + MediatR (CQRS), Minimal APIs con MapGroup anidado, metodos estaticos como handlers,
modulo Carter (ICarterModule), un endpoint FastEndpoints, IUnitOfWork con repositorios como propiedades,
Repository<T> generico, SQL en raw string literals (C# 11) y un .sql embebido cargado con SqlLoader.

Todas las rutas de archivo son relativas a la raiz del fixture (`B-inventario-mediatr/`).

## Endpoints

1. `GET /ping`  (Handler: Program -> lambda, src/Inventario.Api/Program.cs:32)
2. `GET /api/v1/productos`  (Handler: ProductosEndpoints.ListarProductos)
3. `GET /api/v1/productos/{id}`  (Handler: ProductosEndpoints.MapProductosEndpoints -> lambda, src/Inventario.Api/Endpoints/ProductosEndpoints.cs:22)
4. `GET /api/v1/productos/{id}/kardex/{anio}`  (Handler: ProductosEndpoints.ObtenerKardex)
5. `GET /api/v1/productos/{id}/precio`  (Handler: ProductosEndpoints.ObtenerPrecio)
6. `GET /api/v1/productos/{id}/unidades`  (Handler: ProductosEndpoints.MapProductosEndpoints -> lambda, src/Inventario.Api/Endpoints/ProductosEndpoints.cs:31)
7. `POST /api/v1/productos`  (Handler: ProductosEndpoints.MapProductosEndpoints -> lambda, src/Inventario.Api/Endpoints/ProductosEndpoints.cs:34)
8. `POST /api/v1/productos/{id}/ajustes`  (Handler: ProductosEndpoints.MapProductosEndpoints -> lambda, src/Inventario.Api/Endpoints/ProductosEndpoints.cs:40)
9. `DELETE /api/v1/productos/{id}`  (Handler: ProductosEndpoints.MapProductosEndpoints -> lambda, src/Inventario.Api/Endpoints/ProductosEndpoints.cs:47)
10. `GET /api/v1/almacenes`  (Handler: AlmacenesModule.AddRoutes -> lambda, src/Inventario.Api/Endpoints/AlmacenesModule.cs:16)
11. `GET /api/v1/almacenes/{codigo}`  (Handler: AlmacenesModule.AddRoutes -> lambda, src/Inventario.Api/Endpoints/AlmacenesModule.cs:18)
12. `GET /api/v1/almacenes/{codigo}/ubicaciones`  (Handler: AlmacenesModule.AddRoutes -> lambda, src/Inventario.Api/Endpoints/AlmacenesModule.cs:21)
13. `POST /api/v1/almacenes/{codigo}/reservas`  (Handler: AlmacenesModule.ReservarStock)
14. `POST /api/v1/almacenes/{codigo}/cierre`  (Handler: AlmacenesModule.CerrarAlmacen)
15. `GET /api/v1/stock/{sku}`  (Handler: ConsultarStockEndpoint.HandleAsync)

Notas de rutas:
- Endpoints 2-9: `var api = app.MapGroup("/api/v1")` en Program.cs:36 + `api.MapProductosEndpoints()` (Program.cs:37) + `var g = app.MapGroup("/productos")` dentro de la extension (ProductosEndpoints.cs:17). `"/"` dentro del grupo => sin barra final.
- Restricciones eliminadas: `{id:guid}` -> `{id}`, `{anio:int?}` -> `{anio}`, `{codigo:length(3)}` -> `{codigo}` (la restriccion contiene parentesis).
- Endpoints 10-14: grupo absoluto `/api/v1/almacenes` dentro de `ICarterModule.AddRoutes` (registrado con `app.MapCarter()`, fuera del grupo `/api/v1`).
- Endpoint 15: FastEndpoints, ruta absoluta en `Configure() { Get("/api/v1/stock/{sku}"); }` (ConsultarStockEndpoint.cs:23).
- `Results.Created($"/api/v1/productos/{id}", ...)` (ProductosEndpoints.cs:37) NO es un endpoint.

## Filas esperadas

| Endpoint | SP | Hijo | Tipo | Archivo:linea | Notas |
|---|---|---|---|---|---|
| GET /api/v1/productos | PCK_INVENTARIO.SP_LISTAR_PRODUCTOS | PCK_UTIL.FN_UNIDAD_MEDIDA | HIJO | src/Inventario.Infrastructure/Sql/Productos/ListarProductos.sql:10 | Marcador nivel 1 dentro del .sql (`-- Migrado de PCK_INVENTARIO.SP_LISTAR_PRODUCTOS`, linea 3). El .sql se incluye porque C# lo referencia por nombre: `SqlLoader.Load("ListarProductos.sql")` en src/Inventario.Infrastructure/Persistence/ProductoRepository.cs:45 (flujo: ListarProductos -> ListarProductosQueryHandler -> _uow.Productos.ListarAsync). La comilla suelta del comentario SQL de la linea 4 no abre literal. |
| GET /api/v1/productos | PCK_INVENTARIO.SP_LISTAR_PRODUCTOS | PCK_UTIL.FN_UNIDAD_MEDIDA -> PCK_UTIL.FN_FACTOR_CONVERSION | HIJO_N2 | src/Inventario.Infrastructure/Persistence/ProductoRepository.cs:17 | El hijo FN_UNIDAD_MEDIDA esta migrado en otra consulta del repo: const `SqlUnidades` con marcador nivel 2 (ProductoRepository.cs:13) que llama a FN_FACTOR_CONVERSION. Linea = donde aparece el nieto. |
| GET /api/v1/productos/{id} | PCK_UTIL.FN_FORMATEAR_SKU | | DIRECTO_EN_QUERY | src/Inventario.Infrastructure/Persistence/ProductoRepository.cs:35 | Escrito en minusculas `pck_util.fn_formatear_sku(...)`: se reporta en MAYUSCULAS. SELECT sin marcador en ningun nivel (lambda, handler, metodo y clase sin comentarios que nombren SPs). |
| GET /api/v1/productos/{id} | FN_ESTADO_PRODUCTO | | DIRECTO_EN_QUERY | src/Inventario.Infrastructure/Persistence/ProductoRepository.cs:36 | Funcion standalone seguida de `(`. En la misma linea, antes, hay un literal SQL `'--'` que NO es comentario: si se trunca la linea se pierde esta fila. |
| GET /api/v1/productos/{id}/kardex/{anio} | PCK_INVENTARIO.SP_KARDEX_PRODUCTO | | MIGRADO_LISTO | src/Inventario.Api/Endpoints/ProductosEndpoints.cs:62 | Marcador heredado (nivel 5) del `///` del handler estatico ObtenerKardex. La consulta esta 3 capas abajo: ObtenerKardexQueryHandler -> IKardexService/KardexService.ObtenerAsync -> IKardexRepository/KardexRepository.ListarMovimientosAsync (SQL en src/Inventario.Infrastructure/Persistence/KardexRepository.cs:18-28), sin comentario propio y sin llamadas a SPs. |
| GET /api/v1/productos/{id}/precio | PCK_INVENTARIO.SP_OBTENER_PRECIO | PCK_PRECIOS.FN_APLICAR_DESCUENTO | HIJO | src/Inventario.Infrastructure/Persistence/ProductoRepository.cs:61 | Marcador = `Description = "SP: PCK_INVENTARIO.SP_OBTENER_PRECIO"` de `[SwaggerOperation]` sobre ObtenerPrecio (ProductosEndpoints.cs:67), heredado (nivel 5) por la SQL de ObtenerPrecioAsync. Hijo escrito con identificadores entre comillas `"PCK_PRECIOS"."FN_APLICAR_DESCUENTO"`. |
| GET /api/v1/productos/{id}/unidades | PCK_UTIL.FN_UNIDAD_MEDIDA | PCK_UTIL.FN_FACTOR_CONVERSION | HIJO | src/Inventario.Infrastructure/Persistence/ProductoRepository.cs:17 | Marcador nivel 2: comentario directamente sobre la const `SqlUnidades` (ProductoRepository.cs:13); la const se usa en ProductoRepository.cs:73. Linea = donde aparece el hijo dentro del texto de la const. FN_FACTOR_CONVERSION no esta migrada en otro lado => sin HIJO_N2. |
| POST /api/v1/productos | PRC_GENERAR_CODIGO_SKU | | DIRECTO | src/Inventario.Infrastructure/Persistence/ProductoRepository.cs:82 | Nombre standalone desnudo como command text + `CommandType.StoredProcedure` (fin de texto => cuenta). El comentario al final de la misma linea nombra el mismo SP: solo documenta, no genera fila extra. |
| POST /api/v1/productos | PCK_INVENTARIO.SP_CREAR_PRODUCTO | | MIGRADO_LISTO | src/Inventario.Infrastructure/Persistence/ProductoRepository.cs:87 | Marcador nivel 4: XML doc de InsertarAsync. El INSERT (lineas 92-93) no llama SPs. El marcador NO se aplica a GenerarSkuAsync (otro metodo). |
| POST /api/v1/productos/{id}/ajustes | PCK_INVENTARIO.SP_AJUSTAR_STOCK | | DIRECTO | src/Inventario.Infrastructure/Persistence/StockRepository.cs:32 | Nombre interpolado `$"{Paquetes.Inventario}.SP_AJUSTAR_STOCK"` + StoredProcedure; const `Paquetes.Inventario = "PCK_INVENTARIO"` definida en src/Inventario.Infrastructure/Constantes/Paquetes.cs:8. La mencion en `_logger.LogError` (StockRepository.cs:37) es documentacion debil del mismo SP: no es otra fila ni cambia la linea. |
| POST /api/v1/productos/{id}/ajustes | PCK_AUDITORIA.SP_REGISTRAR_EVENTO | | DIRECTO | src/Inventario.Infrastructure/Persistence/AuditoriaRepository.cs:23 | Alcanzado via `_mediator.Publish(new StockAjustadoNotification(...))` (src/Inventario.Application/Productos/Commands/AjustarStock.cs:33) -> RegistrarAuditoriaStockHandler (INotificationHandler). Bloque anonimo `BEGIN ...; END;` solo con la llamada; esquema `INVENTARIO.` descartado. El literal `'PCK_LEGADO.SP_AJUSTE_MANUAL'` se ignora. |
| POST /api/v1/productos/{id}/ajustes | PCK_INVENTARIO.FN_STOCK_MINIMO | | DIRECTO | src/Inventario.Infrastructure/Persistence/StockRepository.cs:46 | Via Publish -> AlertaStockMinimoHandler. `SELECT PCK_INVENTARIO.FN_STOCK_MINIMO@DBL_CENTRAL(:productoId) FROM DUAL`: solo funcion desde DUAL => DIRECTO; db link descartado. |
| DELETE /api/v1/productos/{id} | PCK_SEGURIDAD.FN_USUARIO_ACTUAL | | DIRECTO_EN_QUERY | src/Inventario.Infrastructure/Persistence/ProductoRepository.cs:104 | Funcion dentro de un UPDATE sin marcador (DesactivarProductoCommandHandler -> DesactivarAsync). |
| DELETE /api/v1/productos/{id} | PCK_INVENTARIO.SP_RECALCULAR_DISPONIBLE | | DIRECTO | src/Inventario.Infrastructure/Persistence/StockRepository.cs:64 | `CALL PCK_INVENTARIO.SP_RECALCULAR_DISPONIBLE(:productoId)` (DesactivarProductoCommandHandler -> _uow.Stock.RecalcularDisponibleAsync). |
| GET /api/v1/almacenes | PCK_ALMACEN.SP_LISTAR_ALMACENES | | MIGRADO_LISTO | src/Inventario.Infrastructure/Persistence/AlmacenRepository.cs:20 | Marcador nivel 3: comentario dentro del metodo ListarAsync justo antes de la const local con la SQL (lineas 21-26), ejecutada via el helper base `Repository<T>.QueryAsync`. La SQL no llama SPs (`'X'` es literal). |
| GET /api/v1/almacenes/{codigo}/ubicaciones | PCK_ALMACEN.SP_UBICACIONES_POR_ALMACEN | | MIGRADO_LISTO | src/Inventario.Infrastructure/Persistence/UbicacionRepository.cs:9 | Marcador nivel 6: comentario de clase de UbicacionRepository que nombra exactamente UN SP; niveles 1-5 vacios (lambda, ListarUbicacionesQueryHandler y ListarPorAlmacenAsync sin comentarios con SPs). |
| POST /api/v1/almacenes/{codigo}/reservas | PCK_ALMACEN.SP_RESERVAR_STOCK | | DIRECTO | src/Inventario.Infrastructure/Persistence/AlmacenRepository.cs:37 | Const `Procedimientos.ReservarStock` definida en src/Inventario.Infrastructure/Constantes/Paquetes.cs:18, pasada al wrapper de la clase base `Repository<T>.ExecuteSpAsync(string spName, ...)` (src/Inventario.Infrastructure/Persistence/Repository.cs:31-32, CommandType.StoredProcedure). |
| POST /api/v1/almacenes/{codigo}/cierre | PCK_ALMACEN.SP_CERRAR_ALMACEN | | SOLO_COMENTARIO | src/Inventario.Api/Endpoints/AlmacenesModule.cs:35 | Comentario sobre el handler CerrarAlmacen. El flujo (CerrarAlmacenCommandHandler, src/Inventario.Application/Almacenes/AlmacenesFeature.cs:86 -> AlmacenRepository.EjecutarProcesoAsync, AlmacenRepository.cs:44 -> ExecuteSpAsync) usa un nombre de SP variable que sale de `IOptions<ProcesosOptions>.SpCierreAlmacen`: no resoluble. No hay ninguna otra SQL en el flujo. |
| GET /api/v1/stock/{sku} | PCK_INVENTARIO.SP_CONSULTAR_STOCK | | MIGRADO_LISTO | src/Inventario.Infrastructure/Persistence/StockRepository.cs:52 | Marcador nivel 1: comentario `/* Paquete: PCK_INVENTARIO - SP: SP_CONSULTAR_STOCK */` dentro del raw string; paquete y SP nombrados por separado => PCK_INVENTARIO.SP_CONSULTAR_STOCK (la etiqueta es "Paquete:" en lugar de "Package:"). La SQL no llama SPs. Flujo FastEndpoints sin MediatR: HandleAsync -> _uow.Stock.ConsultarPorSkuAsync. |

Totales: 19 filas = DIRECTO 6, DIRECTO_EN_QUERY 3, MIGRADO_LISTO 5, HIJO 3, HIJO_N2 1, SOLO_COMENTARIO 1.

## Endpoints sin SP

- `GET /ping` - lambda en Program.cs:32, sin acceso a datos.
- `GET /api/v1/almacenes/{codigo}` - accede a BD via `_uow.Almacenes.ObtenerPorCodigoAsync` heredado de la base generica `Repository<T>.ObtenerPorCodigoAsync` (src/Inventario.Infrastructure/Persistence/Repository.cs:24-26): `SELECT * FROM {NombreTabla} ...` sin SPs; ni la clase `Repository<T>` ni los metodos del flujo tienen marcadores.

## Referencias huerfanas

| Archivo:linea | SP | Motivo |
|---|---|---|
| src/Inventario.Infrastructure/Persistence/ProductoRepository.cs:111 | PCK_INVENTARIO.SP_ELIMINAR_PRODUCTO | EliminarAsync solo lo llama EliminarProductoCommandHandler (src/Inventario.Application/Productos/Commands/ProductoCommands.cs:82) y ningun endpoint envia EliminarProductoCommand. |
| src/Inventario.Infrastructure/Persistence/ProductoRepository.cs:119 | PCK_PRECIOS.FN_HISTORICO_PRECIOS | ObtenerHistoricoPreciosAsync no se invoca desde ningun lado (metodo inalcanzable dentro de una clase alcanzable). |
| src/Inventario.Infrastructure/Jobs/RecalculoCostosJob.cs:25 | PCK_COSTOS.SP_RECALCULAR_COSTO_PROMEDIO | Job no registrado en el host ni invocado. |
| src/Inventario.Infrastructure/Constantes/Paquetes.cs:21 | PCK_ALMACEN.SP_LIBERAR_RESERVA | Constante definida pero nunca usada. |

## No debe aparecer

- `tests/Inventario.Application.Tests/AjustarStockCommandHandlerTests.cs:31-33,39` - proyecto de tests (xunit/Moq, carpeta tests/): PCK_INVENTARIO.SP_AJUSTAR_STOCK y PCK_PRUEBAS.SP_CARGAR_SEMILLA no generan filas ni huerfanos. PCK_PRUEBAS.SP_CARGAR_SEMILLA no debe aparecer en ninguna salida.
- `docs/estado-migracion.md` y `docs/db/PCK_INVENTARIO.pkb.sql` - carpeta docs, .md y .sql no referenciados: en particular PCK_INVENTARIO.SP_TRANSFERIR_STOCK no debe aparecer en ninguna salida.
- `src/Inventario.Infrastructure/Sql/Scripts/AnalisisRotacion.sql:3,8` - .sql que ningun codigo C# referencia por nombre (el glob `Sql\**\*.sql` del .csproj lo embebe, pero eso no es una referencia por nombre): PCK_INVENTARIO.FN_INDICE_ROTACION y PCK_INVENTARIO.SP_ANALISIS_ROTACION no aparecen (tampoco como huerfanos).
- `src/Inventario.Infrastructure/bin/Debug/net8.0/Sql/Productos/ListarProductos.sql:3` - copia vieja en bin con el MISMO nombre de archivo; `SqlLoader.Load("ListarProductos.sql")` debe resolver al de `src/Inventario.Infrastructure/Sql/Productos/`. No debe haber un DIRECTO PCK_INVENTARIO.SP_LISTAR_PRODUCTOS para GET /api/v1/productos.
- `src/Inventario.Infrastructure/Persistence/StockRepository.cs:37` - mencion de PCK_INVENTARIO.SP_AJUSTAR_STOCK en `_logger.LogError`: no es llamada; la unica fila de ese SP apunta a la linea 32.
- `src/Inventario.Infrastructure/Persistence/AuditoriaRepository.cs:23` - `'PCK_LEGADO.SP_AJUSTE_MANUAL'` es un literal SQL: se ignora por completo (ni fila, ni marcador, ni huerfano).
- `src/Inventario.Infrastructure/Persistence/ProductoRepository.cs:64` - `PRC_LISTA_PRECIOS` es una tabla (standalone no seguido de `(`/`;`/fin de texto): no es llamada.
- `src/Inventario.Infrastructure/Persistence/ProductoRepository.cs:36` - literal `'--'`: no es comentario (no debe ocultar FN_ESTADO_PRODUCTO).
- `src/Inventario.Infrastructure/Sql/Productos/ListarProductos.sql:4` - apostrofe suelto dentro de un comentario SQL: no abre literal (no debe ocultar FN_UNIDAD_MEDIDA de la linea 10).
- `src/Inventario.Application/Productos/Commands/AjustarStock.cs:30` - string C# `"productos/*"`: no abre comentario `/*` (si lo hiciera se perderia el Publish de la linea 33 y con el las filas de SP_REGISTRAR_EVENTO y FN_STOCK_MINIMO).
- `src/Inventario.Infrastructure/Sql/SqlLoader.cs:19` - string C# con `Sql/*.sql`: no abre comentario. La linea 7 tiene `Sql/**/*.sql` dentro de un comentario `///`.
- `src/Inventario.Api/Program.cs:32` - string con `https://` (contiene `//`) y objeto anonimo con llaves; Program.cs:31 comentario con llaves y comillas.
- Llaves/comillas en comentarios y codigo: ProductosEndpoints.cs:21 (comentario JSON), patrones `is { } x` (ProductosEndpoints.cs:23 y 69, AlmacenesModule.cs:19), ProductoRepository.cs:10 (comentario con `"` desbalanceada), ProductoQueries.cs:35 (comentario con `"*"` y `'%'`), StockAjustadoHandlers.cs:20 (interpolado con `{{`, `}}` y `\"`), Repository.cs:7 (doc con llaves). Ninguno genera filas ni debe romper el parseo.
- PCK_ALMACEN.SP_RESERVAR_STOCK NO debe asociarse a `POST /api/v1/almacenes/{codigo}/cierre` aunque ambos flujos pasan por el mismo wrapper `Repository<T>.ExecuteSpAsync`: el argumento se resuelve por sitio de llamada (en cierre es variable). Igualmente PCK_ALMACEN.SP_CERRAR_ALMACEN no aparece en reservas.
- Fugas de marcadores entre endpoints: SP_KARDEX_PRODUCTO solo en el endpoint kardex; SP_OBTENER_PRECIO solo en precio; SP_CERRAR_ALMACEN solo en cierre; SP_CREAR_PRODUCTO solo en POST /api/v1/productos y solo sobre el INSERT (no sobre PRC_GENERAR_CODIGO_SKU); FN_UNIDAD_MEDIDA como padre migrado solo en unidades.
- PCK_INVENTARIO.SP_ELIMINAR_PRODUCTO NO se asocia a `DELETE /api/v1/productos/{id}` (ese endpoint envia DesactivarProductoCommand; el handler de EliminarProductoCommand es inalcanzable). Resolucion MediatR por tipo de request, no "todos los handlers".
- `Paquetes.cs:8` (`"PCK_INVENTARIO"`) y `Paquetes.cs:10` (`"PCK_ALMACEN"`): nombre de paquete sin miembro, no es SP por si solo.
- Identificadores C# `SpCierreAlmacen`, `nombreSp`, `spName`, parametros `p_*` y literales `'SIN STOCK'`, `'OK'`, `'X'`, `'S'`, `'A'`, `'I'`, `"AJUSTE_STOCK"`: no son nombres de SP.
- `SP_CONSULTAR_STOCK` suelto dentro del comentario de StockRepository.cs:52 no se reporta como standalone: se combina con el paquete.
- El comentario de fin de linea de ProductoRepository.cs:82 no produce MIGRADO_LISTO (nombra el SP que se llama en esa misma linea).

## Escenarios cubiertos

- Minimal API con grupo creado en Program.cs (`app.MapGroup("/api/v1")`) y pasado a un metodo de extension que crea un subgrupo (`app.MapGroup("/productos")`); combinacion de prefijos.
- Handlers lambda (`async (Guid id, ISender sender) => ...`) y method group (metodos estaticos `ListarProductos`, `ObtenerKardex`, `ObtenerPrecio`, `ReservarStock`, `CerrarAlmacen`).
- Modulo Carter (`ICarterModule.AddRoutes`) con grupo absoluto y restricciones `{codigo:length(3)}`.
- Endpoint FastEndpoints (`Endpoint<TReq,TRes>`, `Configure(){ Get(...); }`, `HandleAsync`) sin MediatR.
- Restricciones de ruta y parametro opcional: `{id:guid}`, `{anio:int?}`, `{codigo:length(3)}`; ruta `"/"` en grupo sin barra final.
- MediatR: `IRequest<T>`/`IRequest`, `IRequestHandler<TReq,TRes>`/`IRequestHandler<TReq>`, `ISender.Send`, `IMediator.Publish` con dos `INotificationHandler<T>`; handler de comando nunca enviado (inalcanzable).
- Servicio de aplicacion intermedio (IKardexService -> KardexService) e implementaciones de interfaces.
- IUnitOfWork con repositorios como propiedades (`_uow.Productos.X`, `_uow.Stock.X`, ...) -> UnitOfWork -> repositorio concreto.
- Base generica `Repository<T>`: metodo heredado no sobreescrito (ObtenerPorCodigoAsync) y wrapper `ExecuteSpAsync(string spName, ...)` usado con constante y con variable.
- Raw string literals `"""` (campo const, const local, var, argumento inline) con comillas dobles internas.
- DIRECTO: SP interpolado desde const (`$"{Paquetes.Inventario}.SP_AJUSTAR_STOCK"`), const de clase de constantes via wrapper, standalone desnudo (`PRC_...`), bloque anonimo con esquema, `CALL`, `SELECT fn@dblink(...) FROM DUAL`.
- DIRECTO_EN_QUERY: funcion en SELECT (minusculas y standalone) y en UPDATE.
- MIGRADO_LISTO por marcador de nivel 1 (`/* */` con paquete y SP separados), 3 (comentario en el cuerpo), 4 (XML doc del metodo), 5 (heredado de `///` del handler, 3 capas abajo) y 6 (comentario de clase con un solo SP).
- HIJO por marcador de nivel 1 (.sql embebido), 2 (comentario sobre const) y 5 (atributo `[SwaggerOperation(Description = "SP: ...")]` heredado); hijo con identificadores entre comillas.
- HIJO_N2: hijo de un .sql migrado que esta migrado a su vez en una const C#.
- SOLO_COMENTARIO: nombre de SP variable desde `IOptions<T>` + comentario sobre el handler.
- .sql embebido cargado por nombre con `SqlLoader.Load("ListarProductos.sql")` vs. .sql no referenciado en src y copia homonima en bin.
- Marcadores que coinciden con el SP invocado (comentario de fin de linea, mencion en log) = documentacion, sin filas extra.
- Huerfanos: metodo inalcanzable en clase alcanzable, handler MediatR nunca enviado, job no registrado, constante no usada.
- Exclusiones: proyecto de tests, carpeta docs (.md y .sql), bin, .sql no referenciado.
- Trampas de parseo: `//` y `/*` dentro de strings C#, `--` dentro de literal SQL, apostrofe en comentario SQL, comillas desbalanceadas en comentario C#, llaves en comentarios, en patrones `is { }`, en objetos anonimos y en strings interpolados con `{{ }}`.
- Endpoints sin SP: uno sin BD (`/ping`) y uno con BD pero sin SPs (base generica).

## Notas de interpretacion

- HIJO con la SQL en una const (fila de `/unidades`): la linea es donde aparece el nombre del hijo dentro del texto de la const (ProductoRepository.cs:17), no la linea de uso (73). La regla "linea de uso" se aplico solo a DIRECTO cuyo command text ES el nombre del SP (StockRepository.cs:32, AlmacenRepository.cs:37).
- HIJO_N2: la linea es la del nieto en la consulta donde el hijo esta migrado.
- Un marcador que nombra un SP que el flujo SI invoca (DIRECTO) no produce MIGRADO_LISTO ni SOLO_COMENTARIO. En este fixture los marcadores de handler (`///`, `[SwaggerOperation]`, comentario de CerrarAlmacen) se disenaron para alcanzar UNA sola SQL/llamada, de modo que la herencia (nivel 5) no es ambigua.
- Las menciones en logs/excepciones solo nombran SPs que se invocan en el mismo metodo; no hay menciones de log de SPs no invocados en codigo alcanzable (caso ambiguo evitado).
- La etiqueta del comentario separado es "Paquete:" (espanol) en vez de "Package:"; se espera la misma combinacion PCK_X + SP_Y.
- La constante no usada `Procedimientos.LiberarReserva` se lista como huerfana; los archivos excluidos (tests, docs, bin, .sql no referenciados) no aportan huerfanos.
