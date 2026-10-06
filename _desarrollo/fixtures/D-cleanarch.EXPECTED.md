# Ground truth: fixture D-cleanarch

Copia de `clean-architecture` (minimal APIs con `MapGroup` + métodos estáticos que reciben handlers,
Application con handlers y puertos, Infrastructure con Dapper) a la que se le inyectaron escenarios
de migración Oracle. Todas las rutas de archivo son relativas a la raíz del fixture.

## Endpoints

1. GET /api/products  (Handler: ProductEndpoints.ListProducts)
2. GET /api/products/{id}  (Handler: ProductEndpoints.GetProductById)
3. POST /api/products  (Handler: ProductEndpoints.CreateProduct)
4. PUT /api/products/{id}/price  (Handler: ProductEndpoints.UpdateProductPrice)
5. DELETE /api/products/{id}  (Handler: ProductEndpoints.DeleteProduct)
6. POST /api/orders  (Handler: OrderEndpoints.PlaceOrder)
7. GET /api/orders/{id}  (Handler: OrderEndpoints.GetOrderById)
8. POST /api/orders/{id}/cancel  (Handler: OrderEndpoints.CancelOrder)
9. GET /api/reports/ventas/{anio}  (Handler: ReportEndpoints.GetSalesReport)
10. GET /api/reports/stock-bajo/{umbral}  (Handler: ReportEndpoints.GetLowStock)
11. GET /api/reports/precio-formateado  (Handler: ReportEndpoints.FormatPrice)
12. GET /api/reports/top-productos  (Handler: ReportEndpoints.GetTopProducts)
13. GET /api/reports/monedas  (Handler: ReportEndpoints.ListCurrencies)
14. POST /api/reports/cierre-mensual  (Handler: ReportEndpoints.RunMonthlyClose)
15. GET /api/reports/clientes/{clienteId}/resumen  (Handler: ReportEndpoints.GetClientSummary)
16. GET /api/reports/tipo-cambio/{moneda}  (Handler: ReportEndpoints.MapReportEndpoints (lambda en src/CleanArchitecture.Api/Reports/ReportEndpoints.cs:57))
17. GET /  (Handler: Program (lambda top-level en src/CleanArchitecture.Api/Program.cs:106))

Notas sobre rutas:
- `/api/products` y `/api/orders`: `MapGroup` dentro de `MapProductEndpoints`/`MapOrderEndpoints`; `MapGet("/")`/`MapPost("/")` sobre el grupo dan la ruta del grupo sin barra final.
- `/api/reports/...`: el grupo `/api` se crea en `Program.cs:117` y se pasa a la extensión (`api.MapReportEndpoints()`, `Program.cs:118`); la extensión agrega `/reports` (`ReportEndpoints.cs:25`) y un subgrupo `/clientes` (`ReportEndpoints.cs:48`).
- Restricciones eliminadas: `{id:guid}` -> `{id}`, `{anio:int}` -> `{anio}`, `{umbral:int?}` -> `{umbral}`, `{clienteId:long}` -> `{clienteId}`, `{moneda:alpha:length(3)}` -> `{moneda}`.
- `GET /` está dentro de `if (app.Environment.IsDevelopment())` y lleva `.ExcludeFromDescription()`, pero es un endpoint mapeado en código: se lista (sin filas). `app.MapOpenApi()` y `UseSwaggerUI` son endpoints del framework y NO se listan.

## Filas esperadas

| Endpoint | SP | Hijo | Tipo | Archivo:linea | Notas |
|---|---|---|---|---|---|
| GET /api/products | PCK_PRODUCTOS.SP_LISTAR_PRODUCTOS | PCK_UTIL.FN_FORMATEAR_PRECIO | HIJO | src/CleanArchitecture.Infrastructure/Persistence/Products/ProductQueries.cs:44 | Marcador nivel 3 (bloque de comentarios dentro de ProductQueries.ListAsync) en ProductQueries.cs:25. Una sola consulta regular (COUNT(*) OVER + ROWNUM). El literal 'PCK_LEGADO.SP_IMPORTAR_CATALOGO' de la línea 47 se ignora. |
| GET /api/products | PCK_PRODUCTOS.SP_LISTAR_PRODUCTOS | PCK_UTIL.FN_FORMATEAR_PRECIO -> PCK_UTIL.FN_SIMBOLO_MONEDA | HIJO_N2 | src/CleanArchitecture.Infrastructure/Persistence/Reports/ReportQueries.cs:66 | El hijo FN_FORMATEAR_PRECIO está migrado en otra consulta regular (ReportQueries.FormatearPrecioAsync, marcador al final de la línea 66). Linea = donde aparece el nieto. |
| GET /api/products | PCK_PRODUCTOS.SP_LISTAR_PRODUCTOS | PCK_UTIL.FN_FORMATEAR_PRECIO -> PCK_UTIL.FN_REDONDEAR | HIJO_N2 | src/CleanArchitecture.Infrastructure/Persistence/Reports/ReportQueries.cs:66 | Igual que la fila anterior (segundo nieto, misma línea). |
| GET /api/products/{id} | PCK_PRODUCTOS.SP_OBTENER_PRODUCTO |  | MIGRADO_LISTO | src/CleanArchitecture.Infrastructure/Persistence/Products/ProductRepository.cs:41 | Nivel 4: comentario inmediatamente encima de la firma de ProductRepository.GetByIdAsync. La consulta (línea 51) usa la const SelectColumns (línea 38, su comentario de la línea 37 no nombra SP) y no llama SP. |
| POST /api/products | PCK_PRODUCTOS.FN_EXISTE_SKU |  | DIRECTO | src/CleanArchitecture.Infrastructure/Persistence/Products/ProductRepository.cs:70 | SELECT de una función FROM DUAL con esquema (CATALOGO.PCK_PRODUCTOS...), el esquema se descarta. El LogError de la línea 79 nombra la misma función: solo documenta, no genera otra fila. |
| POST /api/products | PCK_UTIL.FN_NORMALIZAR_NOMBRE |  | DIRECTO_EN_QUERY | src/CleanArchitecture.Infrastructure/Persistence/Products/ProductRepository.cs:97 | Escrita en minúsculas (pck_util.fn_normalizar_nombre) dentro del INSERT de ProductRepository.AddAsync; sin marcador en ningún nivel. |
| PUT /api/products/{id}/price | PCK_PRODUCTOS.SP_OBTENER_PRODUCTO |  | MIGRADO_LISTO | src/CleanArchitecture.Infrastructure/Persistence/Products/ProductRepository.cs:41 | UpdateProductPriceHandler -> IProductRepository.GetByIdAsync. |
| PUT /api/products/{id}/price | PCK_UTIL.FN_FECHA_SERVIDOR |  | DIRECTO_EN_QUERY | src/CleanArchitecture.Infrastructure/Persistence/Products/ProductRepository.cs:134 | ProductRepository.UpdateAsync. El bloque de comentarios previo (línea 125) nombra la MISMA función que se llama: solo documenta, no la convierte en MIGRADO. |
| DELETE /api/products/{id} | FN_LINEA_ACTIVA |  | DIRECTO_EN_QUERY | src/CleanArchitecture.Infrastructure/Persistence/Orders/OrderRepository.cs:159 | DeleteProductHandler -> IOrderRepository.ExistsWithProductAsync. Función independiente calificada con esquema (VENTAS.FN_LINEA_ACTIVA): se reporta sin esquema. |
| DELETE /api/products/{id} | PCK_PRODUCTOS.SP_ELIMINAR_PRODUCTO |  | DIRECTO | src/CleanArchitecture.Infrastructure/Persistence/Products/ProductRepository.cs:154 | Const ProcedimientosOracle.EliminarProducto definida en src/CleanArchitecture.Infrastructure/Persistence/ProcedimientosOracle.cs:15 como $"{PaqueteProductos}.SP_ELIMINAR_PRODUCTO" (PaqueteProductos = "PCK_PRODUCTOS" en la línea 12). Se ejecuta con el wrapper DbSession.ExecuteSpAsync (CommandType.StoredProcedure). |
| POST /api/orders | PCK_PRODUCTOS.SP_OBTENER_PRODUCTO |  | MIGRADO_LISTO | src/CleanArchitecture.Infrastructure/Persistence/Products/ProductRepository.cs:41 | PlaceOrderHandler -> IProductRepository.GetByIdAsync. |
| POST /api/orders | PCK_PEDIDOS.SP_INSERTAR_PEDIDO |  | MIGRADO_LISTO | src/CleanArchitecture.Infrastructure/Persistence/Orders/OrderRepository.cs:71 | Nivel 3 sobre el INSERT de la línea 76 (sin SP). Su alcance termina en el siguiente bloque que nombra SP (línea 85), así que NO cubre el bloque anónimo. InsertLinesAsync (sin marcadores, ni propios ni heredados) no genera filas. |
| POST /api/orders | PCK_PEDIDOS.SP_REGISTRAR_AUDITORIA |  | DIRECTO | src/CleanArchitecture.Infrastructure/Persistence/Orders/OrderRepository.cs:92 | Bloque anónimo BEGIN ... END; compuesto solo de llamadas. El comentario de la línea 86 nombra esta misma SP: solo documenta. |
| POST /api/orders | PCK_CONTABILIDAD.SP_REGISTRAR_VENTA |  | DIRECTO | src/CleanArchitecture.Infrastructure/Persistence/Orders/OrderRepository.cs:93 | Llamada por db link (SP_REGISTRAR_VENTA@ERP_LINK); el @LINK se descarta. |
| POST /api/orders | PRC_ACTUALIZAR_RESUMEN_VENTAS |  | DIRECTO | src/CleanArchitecture.Infrastructure/Persistence/Orders/OrderRepository.cs:94 | Procedimiento independiente sin paréntesis, seguido de ";" dentro del bloque anónimo. |
| POST /api/orders | PCK_UTIL.FN_FECHA_SERVIDOR |  | DIRECTO_EN_QUERY | src/CleanArchitecture.Infrastructure/Persistence/Products/ProductRepository.cs:134 | PlaceOrderHandler -> IProductRepository.UpdateAsync (no confundir con OrderRepository.UpdateAsync). |
| POST /api/orders | PKG_NOTIFICACIONES.SP_ENCOLAR_CORREO |  | DIRECTO | src/CleanArchitecture.Infrastructure/Notifications/OracleNotificationSender.cs:15 | Const ProcedimientosOracle.EncolarCorreo definida en src/CleanArchitecture.Infrastructure/Persistence/ProcedimientosOracle.cs:18 (prefijo PKG_), ejecutada con DbSession.ExecuteSpAsync. Camino: IDomainEventDispatcher.DispatchAsync -> DomainEventDispatcher -> IDomainEventHandler.HandleAsync -> DomainEventHandler<T> (base abstracta) -> NotifyWhenOrderPlaced/NotifyWhenOrderCancelled.HandleAsync -> INotificationSender.SendAsync -> OracleNotificationSender.SendAsync. |
| GET /api/orders/{id} | PCK_PEDIDOS.SP_OBTENER_CABECERA_PEDIDO | PCK_PEDIDOS.FN_ESTADO_DESC | HIJO | src/CleanArchitecture.Infrastructure/Persistence/Orders/OrderRepository.cs:29 | Nivel 2: comentario directamente encima de la const ObtenerCabeceraSql (marcador en la línea 25); la const se usa en la línea 40. |
| GET /api/orders/{id} | PCK_PEDIDOS.SP_CONSULTAR_PEDIDO_COMPLETO |  | MIGRADO_LISTO | src/CleanArchitecture.Api/Orders/OrderEndpoints.cs:85 | Nivel 5 (heredado): el <remarks> de la acción GetOrderById se aplica a la consulta SIN marcador de líneas de pedido (OrderRepository.cs:52), que no llama SP. GetOrderByIdHandler.ExecuteAsync no tiene marcadores, así que se sube hasta la acción. |
| POST /api/orders/{id}/cancel | PCK_PEDIDOS.SP_OBTENER_CABECERA_PEDIDO | PCK_PEDIDOS.FN_ESTADO_DESC | HIJO | src/CleanArchitecture.Infrastructure/Persistence/Orders/OrderRepository.cs:29 | CancelOrderHandler -> IOrderRepository.GetByIdAsync. En este camino la consulta de líneas (OrderRepository.cs:52) NO hereda ningún marcador (CancelOrder y su handler no tienen). |
| POST /api/orders/{id}/cancel | PCK_PRODUCTOS.SP_OBTENER_PRODUCTO |  | MIGRADO_LISTO | src/CleanArchitecture.Infrastructure/Persistence/Products/ProductRepository.cs:41 | CancelOrderHandler -> IProductRepository.GetByIdAsync (dentro del foreach de líneas). |
| POST /api/orders/{id}/cancel | PCK_UTIL.FN_FECHA_SERVIDOR |  | DIRECTO_EN_QUERY | src/CleanArchitecture.Infrastructure/Persistence/Products/ProductRepository.cs:134 | CancelOrderHandler -> IProductRepository.UpdateAsync. |
| POST /api/orders/{id}/cancel | PCK_PEDIDOS.SP_ACTUALIZAR_ESTADO_PEDIDO |  | MIGRADO_LISTO | src/CleanArchitecture.Infrastructure/Persistence/Orders/OrderRepository.cs:115 | Nivel 1: comentario -- dentro del propio texto del UPDATE (OrderRepository.UpdateAsync), que no llama SP. El DELETE de la línea 134 no tiene marcador ni SP. |
| POST /api/orders/{id}/cancel | PRC_RECALCULAR_STOCK |  | DIRECTO | src/CleanArchitecture.Infrastructure/Persistence/Dal/InventarioDal.cs:19 | CALL a procedimiento independiente en la clase DAL estática InventarioDal.RecalcularStockAsync, invocada desde OrderRepository.UpdateAsync (OrderRepository.cs:143). |
| POST /api/orders/{id}/cancel | PKG_NOTIFICACIONES.SP_ENCOLAR_CORREO |  | DIRECTO | src/CleanArchitecture.Infrastructure/Notifications/OracleNotificationSender.cs:15 | Mismo camino de eventos de dominio que en POST /api/orders; const en ProcedimientosOracle.cs:18. |
| GET /api/reports/ventas/{anio} | PCK_REPORTES.SP_VENTAS_POR_MES | PCK_UTIL.FN_NOMBRE_MES | HIJO | src/CleanArchitecture.Infrastructure/Queries/Reportes/VentasPorMes.sql:8 | .sql incrustado, referenciado por nombre en ReportQueries.cs:26 (SqlResources.Load("Queries.Reportes.VentasPorMes.sql")). Marcador nivel 1 dentro del bloque /* */ del propio .sql (VentasPorMes.sql:3). |
| GET /api/reports/stock-bajo/{umbral} | PCK_INVENTARIO.FN_STOCK_DISPONIBLE |  | DIRECTO_EN_QUERY | src/CleanArchitecture.Infrastructure/Persistence/Reports/ReportQueries.cs:48 | Identificadores entre comillas ("PCK_INVENTARIO"."FN_STOCK_DISPONIBLE"). En la misma línea, antes de la llamada, hay un literal '-- sin nota --' que NO es comentario. El /* */ de la línea 51 no nombra SP. |
| GET /api/reports/precio-formateado | PCK_UTIL.FN_FORMATEAR_PRECIO | PCK_UTIL.FN_SIMBOLO_MONEDA | HIJO | src/CleanArchitecture.Infrastructure/Persistence/Reports/ReportQueries.cs:66 | Marcador nivel 3 escrito como comentario al final de la misma línea del SQL. Es una consulta regular sobre la tabla Monedas (no FROM DUAL), por eso es migración y no DIRECTO. |
| GET /api/reports/precio-formateado | PCK_UTIL.FN_FORMATEAR_PRECIO | PCK_UTIL.FN_REDONDEAR | HIJO | src/CleanArchitecture.Infrastructure/Persistence/Reports/ReportQueries.cs:66 | Segundo hijo, misma línea. |
| GET /api/reports/top-productos | PCK_REPORTES.SP_TOP_PRODUCTOS |  | DIRECTO | src/CleanArchitecture.Infrastructure/Persistence/Reports/ReportQueries.cs:86 | Cadena que es exactamente el nombre de la SP con CommandType.StoredProcedure. El LogError (línea 96) y el InvalidOperationException (línea 97) nombran la misma SP: no generan filas. |
| POST /api/reports/cierre-mensual | PCK_REPORTES.SP_CIERRE_MENSUAL |  | SOLO_COMENTARIO | src/CleanArchitecture.Api/Reports/ReportEndpoints.cs:109 | Atributo [EndpointDescription("Package: PCK_REPORTES - SP: SP_CIERRE_MENSUAL")] (paquete y SP nombrados por separado). El flujo MonthlyCloseHandler -> IProcesosBatch -> ProcesosBatchRepository -> DbSession.ExecuteSpAsync ejecuta un nombre leído de configuración (IOptions, no resoluble) y no hay ningún SQL al que heredar el marcador. |
| GET /api/reports/clientes/{clienteId}/resumen | PCK_CLIENTES.SP_RESUMEN_CLIENTE |  | MIGRADO_LISTO | src/CleanArchitecture.Infrastructure/Persistence/Reports/ClientSummaryQueries.cs:8 | Nivel 6: comentario de la clase ClientSummaryQueries que nombra exactamente UNA SP. La consulta (línea 19) no tiene marcadores de nivel 1 a 5 ni llama SP. |
| GET /api/reports/tipo-cambio/{moneda} | PCK_UTIL.FN_TIPO_CAMBIO |  | DIRECTO | src/CleanArchitecture.Infrastructure/Persistence/Reports/ReportQueries.cs:104 | Endpoint lambda -> ExchangeRateHandler -> IReportQueries.GetTipoCambioAsync -> método heredado OracleQueryBase.EjecutarFuncionAsync, que arma "SELECT {funcion}(:valor) FROM DUAL" (OracleQueryBase.cs:33). El nombre llega como cadena desnuda. |

Totales: 33 filas (DIRECTO 10, DIRECTO_EN_QUERY 6, MIGRADO_LISTO 8, HIJO 6, HIJO_N2 2, SOLO_COMENTARIO 1).

## Endpoints sin SP

- GET /  (Handler: lambda top-level en Program.cs:106): solo `Results.Redirect("/swagger")`. El resto del código top-level de Program.cs (por ejemplo `MigrateDatabaseAsync`) NO es parte de su flujo.
- GET /api/reports/monedas  (Handler: ReportEndpoints.ListCurrencies): la consulta de ReportQueries.ListarMonedasAsync (líneas 114-117) solo contiene la columna `SP_ORIGEN` (no seguida de "(" ni ";") y el literal `'PCK_LEGADO.SP_BAJA_MONEDA'`.

## Referencias huerfanas

| Archivo:linea | SP | Motivo |
|---|---|---|
| src/CleanArchitecture.Infrastructure/Persistence/Products/ProductRepository.cs:169 | PCK_PRODUCTOS.SP_LISTAR_DESCONTINUADOS | ProductRepository.ListarDescontinuadosAsync no está en IProductRepository y nadie lo llama (misma clase que métodos alcanzables). |
| src/CleanArchitecture.Infrastructure/Persistence/Dal/InventarioDal.cs:27 | PRC_RECONSTRUIR_INDICES | InventarioDal.ReconstruirIndicesAsync: método estático sin llamadas (su vecino RecalcularStockAsync sí es alcanzable). |
| src/CleanArchitecture.Infrastructure/Persistence/Orders/OrderArchiver.cs:24 | PCK_PEDIDOS.SP_ARCHIVAR_PEDIDOS | Clase OrderArchiver no registrada en DI ni usada. |
| src/CleanArchitecture.Infrastructure/Persistence/DatabaseMigrator.cs:77 | PCK_ADMIN.SP_COMPILAR_INVALIDOS | Solo alcanzable desde el arranque (Program.cs:124 -> DependencyInjection.MigrateDatabaseAsync -> DatabaseMigrator.MigrateAsync), no desde un endpoint. |

## No debe aparecer

- `docs/migracion-oracle.md`: carpeta docs y archivo .md (menciona PCK_FACTURACION.SP_EMITIR_FACTURA, PCK_FACTURACION.SP_ANULAR_FACTURA, PRC_REPROCESAR_COLA, PKG_LEGADO.SP_SINCRONIZAR_STOCK y SP ya listadas). Ni filas ni huérfanas.
- `docs/sql/dependencias_paquetes.sql`: .sql en docs y no referenciado (PCK_FACTURACION.FN_TOTAL_FACTURA, PCK_FACTURACION.SP_ANULAR_FACTURA).
- `db/scripts/pck_pedidos_body.sql`: script del DBA no referenciado por C# (repite SP_INSERTAR_PEDIDO, SP_REGISTRAR_AUDITORIA y PRC_RECALCULAR_STOCK como llamadas reales; no deben sumar filas ni cambiar tipos).
- `db/analisis/inventario_procedimientos.txt`: archivo .txt.
- `src/CleanArchitecture.Infrastructure/Queries/Reportes/VentasPorMes_v1.sql`: está en la carpeta Queries e incluido por el glob `EmbeddedResource Include="Queries\**\*.sql"` del .csproj, pero ningún código lo referencia por nombre: PCK_REPORTES.SP_VENTAS_POR_MES_V1 no aparece (ni fila ni huérfana).
- `src/CleanArchitecture.Infrastructure/bin/Debug/net10.0/Queries/Reportes/VentasPorMes.sql`: mismo nombre de archivo que el .sql referenciado, pero en bin/: PCK_UTIL.FN_NOMBRE_MES_OLD no debe aparecer y el HIJO de ventas debe apuntar al .sql de `src/.../Queries/Reportes/`.
- `src/CleanArchitecture.Infrastructure/obj/Debug/net10.0/ProcsGenerados.g.cs`: código generado en obj/ (PCK_GENERADO.SP_DESDE_OBJ, PCK_PRODUCTOS.SP_OBTENER_PRODUCTO).
- `tests/` (proyectos xUnit): `InMemoryProductRepository.cs` (comentarios con PCK_PRODUCTOS.SP_OBTENER_PRODUCTO y PCK_PRODUCTOS.FN_EXISTE_SKU) y `OracleProcedureNamesTests.cs` (bloque mock PCK_PRUEBAS.SP_MOCK_STOCK y asserts con nombres de SP). Los fakes de tests que implementan IProductRepository/IOrderRepository/INotificationSender no son implementaciones a seguir.
- Literales SQL entre comillas simples: `'PCK_LEGADO.SP_IMPORTAR_CATALOGO'` (ProductQueries.cs:47) y `'PCK_LEGADO.SP_BAJA_MONEDA'` (ReportQueries.cs:116).
- Columna `SP_ORIGEN` (ReportQueries.cs:114 y 116): nombre con forma de SP no seguido de "(" ni ";" ni fin de texto: no es llamada. GET /api/reports/monedas queda sin filas.
- Menciones en log/excepciones de SP que SÍ se llaman en el mismo método: ProductRepository.cs:79 (FN_EXISTE_SKU), ReportQueries.cs:96 y 97 (SP_TOP_PRODUCTOS). No generan filas extra ni cambian la línea de la fila DIRECTO.
- Comentarios que nombran la SP que se llama en el SQL siguiente: ProductRepository.cs:125 (FN_FECHA_SERVIDOR) y OrderRepository.cs:86 (SP_REGISTRAR_AUDITORIA). No hay MIGRADO_LISTO ni SOLO_COMENTARIO para ellas.
- Literal `'-- sin nota --'` (ReportQueries.cs:48): no es comentario; si se tratara como tal se perdería FN_STOCK_DISPONIBLE.
- `/*` dentro de un comentario `//` (ReportQueries.cs:16) y dentro de la cadena `FichaFuncional` (ReportQueries.cs:18): no abren comentario de bloque. Si se trataran como tal, el texto hasta el `*/` de la línea 51 se tragaría la referencia al .sql de ventas y FN_STOCK_DISPONIBLE.
- Llave suelta `{` dentro de un comentario con comillas (ReportQueries.cs:41): no debe romper los límites de métodos. GET /api/reports/stock-bajo/{umbral} tiene UNA sola fila (no hereda filas de FormatearPrecioAsync, GetTopProductosAsync, etc.).
- Métodos con el mismo nombre en tipos distintos (GetByIdAsync, AddAsync, UpdateAsync en ProductRepository y OrderRepository): la resolución es por tipo. Por ejemplo: POST /api/orders NO tiene SP_ACTUALIZAR_ESTADO_PEDIDO, PRC_RECALCULAR_STOCK ni SP_OBTENER_CABECERA_PEDIDO; POST /api/products NO tiene SP_INSERTAR_PEDIDO, SP_REGISTRAR_AUDITORIA ni las demás del bloque anónimo; GET /api/products/{id} y PUT /api/products/{id}/price NO tienen SP_OBTENER_CABECERA_PEDIDO ni SP_ACTUALIZAR_ESTADO_PEDIDO; GET /api/orders/{id} NO tiene SP_OBTENER_PRODUCTO.
- `nameof(GetOrderById)` (OrderEndpoints.cs:81) y `nameof(GetProductById)` (ProductEndpoints.cs:157) no son llamadas: POST /api/orders no recibe SP_CONSULTAR_PEDIDO_COMPLETO ni FN_ESTADO_DESC; POST /api/products no recibe SP_OBTENER_PRODUCTO.
- POST /api/orders/{id}/cancel NO tiene MIGRADO_LISTO de PCK_PEDIDOS.SP_CONSULTAR_PEDIDO_COMPLETO (la herencia de nivel 5 depende del camino).
- GET /api/orders/{id} NO tiene SOLO_COMENTARIO de SP_CONSULTAR_PEDIDO_COMPLETO (el marcador quedó asociado a la consulta de líneas).
- POST /api/reports/cierre-mensual NO tiene filas DIRECTO/MIGRADO: la SP real sale de configuración (`appsettings.json` tiene `"CierreMensual": ""`).
- `"PCK_PRODUCTOS"` solo (ProcedimientosOracle.cs:12) no es un miembro de paquete; las definiciones de const de ProcedimientosOracle.cs (líneas 15 y 18) no generan filas propias ni huérfanas (se usan desde código alcanzable).
- Marcadores de posición en comentarios ("PAQUETE.PROCEDIMIENTO" en DbSession.cs, "PAQUETE.FUNCION" en OracleQueryBase.cs) y la plantilla `$"SELECT {funcion}(:valor) FROM DUAL"` no son nombres de SP.
- Funciones nativas de Oracle (NVL, TO_CHAR, EXTRACT, COUNT, SUM, MAX) no son SP.
- LoggingNotificationSender (implementación de INotificationSender que ya no se registra) se recorre pero no aporta nada.

## Escenarios cubiertos

- Minimal APIs: `MapGroup` local (`/api/products`, `/api/orders`), grupo creado en Program.cs y pasado a una extensión (`/api` + `/reports`), subgrupo anidado (`/clientes`), handler como method group estático, handler lambda, ruta raíz del grupo (`"/"`) sin barra final, endpoint `GET /` solo en desarrollo.
- Limpieza de restricciones de ruta: `:guid`, `:int`, `:int?` (opcional), `:long`, `:alpha:length(3)`.
- Grafo de llamadas: endpoint -> handler de Application (parámetro del método) -> interfaz de Domain/Application -> implementación en Infrastructure; clase base (`OracleQueryBase`), clase DAL estática (`InventarioDal`, `SqlResources`), wrapper `DbSession.ExecuteSpAsync`, eventos de dominio (interfaz -> base abstracta -> overrides -> otra interfaz con dos implementaciones).
- Misma SP bajo varios endpoints: SP_OBTENER_PRODUCTO (4 endpoints), FN_FECHA_SERVIDOR (3), SP_OBTENER_CABECERA_PEDIDO/FN_ESTADO_DESC (2), SP_ENCOLAR_CORREO (2).
- DIRECTO: SELECT función FROM DUAL con esquema; bloque anónimo con tres llamadas (paquete, db link, independiente sin paréntesis con ";"); CALL a independiente; cadena desnuda con StoredProcedure; const de clase de constantes; const interpolada desde otra const; nombre pasado a un método heredado que arma el SQL.
- DIRECTO_EN_QUERY: función en INSERT (minúsculas), en UPDATE, independiente calificada con esquema en WHERE, identificadores entre comillas.
- MIGRADO_LISTO por nivel 1 (comentario -- en el SQL), nivel 3 (bloque en el método, con alcance cortado por el siguiente bloque que nombra SP), nivel 4 (comentario sobre la firma), nivel 5 (/// remarks de la acción heredado por una consulta del repositorio, dependiente del camino), nivel 6 (comentario de clase con una sola SP).
- HIJO por nivel 1 en .sql incrustado, nivel 2 (comentario sobre const SQL), nivel 3 (bloque en el método y comentario al final de la línea). HIJO_N2 encadenando una migración de otro endpoint.
- SOLO_COMENTARIO: atributo `[EndpointDescription]` con paquete y SP por separado ("Package: X - SP: Y") y nombre de SP variable (configuración).
- Marcadores que documentan una llamada existente (comentario y log) sin generar migración.
- Archivos .sql incluidos solo si se referencian por nombre desde C# (embedded resource) frente a .sql no referenciados (mismo directorio, docs/, db/, bin/).
- Huérfanas: método público fuera de la interfaz, método estático sin llamadas, clase no registrada, código alcanzable solo desde el arranque.
- Trampas de parseo: `//` y `/*` dentro de cadenas y comentarios, `--` dentro de literal SQL, llave suelta en comentario, comillas en comentarios, cadena verbatim con barras invertidas (OracleQueryBase.cs:11), raw strings `"""`, `nameof(...)` que no es llamada, métodos homónimos en tipos distintos.

## Notas de interpretación

- HIJO_N2: la columna Archivo:linea apunta a la línea donde aparece el NIETO (en la consulta que migró al hijo), porque la regla no fija otra.
- Nivel 5: se toma el llamador más cercano HACIA ARRIBA que tenga marcadores de método (los intermedios sin marcadores se saltan, como en el ejemplo de la regla) y se evalúa por camino de cada endpoint.
- Huérfanas: solo código C# de producción (fuera de tests, bin/obj y docs) que no es alcanzable desde ningún endpoint, aunque sí lo sea desde el arranque. Los archivos excluidos (tests, docs, .md/.txt, .sql no referenciados, bin/obj) no generan ni filas ni huérfanas.
- Nombres independientes calificados con esquema se reportan sin esquema (VENTAS.FN_LINEA_ACTIVA -> FN_LINEA_ACTIVA).
- Para SOLO_COMENTARIO cuyo marcador es un atributo, la línea es la del atributo.
- Una fila por combinación (Endpoint, SP, Hijo, Tipo); si un endpoint alcanza el mismo método por varios caminos, no se duplica.
