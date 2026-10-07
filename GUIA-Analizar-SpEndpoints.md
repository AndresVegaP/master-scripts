# Guía de uso: `Analizar-SpEndpoints.ps1`

Script para saber, **por cada endpoint** de una API .NET + Dapper (OpenAPI/Swagger), qué **stored procedures de Oracle** usa y en qué estado está su migración:

- **SP listos**: el SP fue migrado a una query y esa query ya no llama a ningún otro SP.
- **SP migrados con hijos**: el SP fue migrado a una query, pero esa query todavía llama a otros SP (los hijos), que siguen pendientes.
- **SP llamados directamente**: el endpoint sigue ejecutando el SP tal cual. Está pendiente.

El resultado es un `.md` de lectura rápida. Opcionalmente también genera un `.csv` para Excel.

---

## 1. Requisitos

| Requisito | Detalle |
|---|---|
| PowerShell | **Windows PowerShell 5.1** (el que trae Windows) o PowerShell 7+. |
| Dependencias | Ninguna. No necesita el SDK de .NET, no compila tu solución y no se conecta a la base de datos. |
| Modo de lenguaje | `FullLanguage` (el normal). El script compila su motor de análisis en memoria con `Add-Type`. |
| Codificación del script | **UTF-8 con BOM**. Si lo editas, guárdalo igual (en VS Code: "Save with Encoding → UTF-8 with BOM"). |

## 2. Instalación

1. Descarga el repositorio como ZIP desde GitHub (*Code > Download ZIP*) y extráelo en una ruta corta, por ejemplo `C:\herramientas\`. Las rutas de los repos de prueba son profundas, y el Explorador de Windows no extrae rutas de más de 260 caracteres.
2. Para usarlo basta con el archivo `Analizar-SpEndpoints.ps1`. Puedes copiarlo a cualquier carpeta, por ejemplo `repo/docs/local/scripts/`. Las carpetas `_desarrollo/` y los `.md` solo sirven para mantenerlo.
3. Los archivos descargados de internet quedan marcados como bloqueados. Si tu equipo lo permite, desbloquéalos una vez:

```powershell
Get-ChildItem -Recurse C:\herramientas\master-scripts-main | Unblock-File
```

4. Si no puedes desbloquearlos, o la política de ejecución de tu equipo no permite scripts, ejecútalo así (no cambia la configuración del equipo):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Analizar-SpEndpoints.ps1
```

## 3. Uso rápido

**Sin parámetros:** analiza la carpeta donde está el script y deja `reporte-sp-endpoints.md` junto al script.

```powershell
.\Analizar-SpEndpoints.ps1
```

**Indicando qué carpeta analizar y dónde dejar el reporte.** Las rutas pueden ser relativas a la carpeta desde donde lo ejecutas:

```powershell
.\Analizar-SpEndpoints.ps1 -RepoPath ..\..\..\carpeta-api\src -OutputPath ..\reportes\sp-por-endpoint.md
```

**Con CSV para Excel y con el árbol de llamadas de cada endpoint:**

```powershell
.\Analizar-SpEndpoints.ps1 -RepoPath C:\repos\mi-api -ExportCsv -IncludeTrace
```

**Cruzando con el Swagger** (archivo exportado o URL de la API levantada en local):

```powershell
.\Analizar-SpEndpoints.ps1 -RepoPath C:\repos\mi-api -SwaggerPath https://localhost:5001/swagger/v1/swagger.json
```

Si `-OutputPath` apunta a una carpeta, el reporte se crea dentro con el nombre `reporte-sp-endpoints.md`. El reporte siempre es `.md`: si indicas otra extensión (p. ej. `reporte.csv`), el script avisa y usa `reporte.md`. Con `-ExportCsv`, el `.csv` se crea al lado, con el mismo nombre.

**Con `-File`** (por ejemplo, con `-ExecutionPolicy Bypass`), PowerShell recibe los parámetros de lista como texto. Escríbelos separados por coma: `-PackagePrefixes PCK_,PKG_,PK_` o `-ExcludePath "src/Legacy/*,*Migrations*"`.

## 4. Parámetros

| Parámetro | Alias | Por defecto | Para qué sirve |
|---|---|---|---|
| `-RepoPath` | `-Repo`, `-Ruta` | Carpeta del script | Carpeta a analizar: la raíz del repo o una subcarpeta (p. ej. `repo\carpeta-api\src`). |
| `-OutputPath` | `-Salida`, `-Output` | `<carpeta del script>\reporte-sp-endpoints.md` | Ruta del `.md`, o una carpeta donde crearlo. |
| `-PackagePrefixes` | | `PCK_`, `PKG_` | Prefijos de los packages Oracle. |
| `-ObjectPrefixes` | | `SP_`, `FN_`, `PRC_` | Prefijos de procedimientos y funciones sin package. |
| `-ExcludePath` | | (vacío) | Patrones comodín, relativos al repo, para excluir más carpetas o archivos. Ejemplo: `-ExcludePath 'src/Legacy/*','*Migrations*'`. |
| `-IncludeTests` | | Desactivado | Incluye los proyectos de test, que por defecto se excluyen para no contar SP de mocks. |
| `-ExportCsv` | | Desactivado | Genera además un `.csv` (separado por `;`, UTF-8) con todas las filas, la forma de llamada y la traza de métodos. |
| `-IncludeTrace` | | Desactivado | Agrega al final del `.md` el árbol de métodos recorrido por cada endpoint, para auditar el resultado. |
| `-SwaggerPath` | | (vacío) | `swagger.json` u `openapi.json` (archivo o URL) para comparar los endpoints documentados con los detectados en el código. |
| `-MaxDepth` | | `40` | Profundidad máxima del recorrido de llamadas. Si algún endpoint la alcanza, el reporte lo indica con la advertencia "Profundidad máxima" (sección 7). |

Ejemplo con prefijos propios:

```powershell
.\Analizar-SpEndpoints.ps1 -PackagePrefixes 'PCK_','PKG_','PK_' -ObjectPrefixes 'SP_','FN_','PRC_','F_'
```

## 5. Qué contiene el reporte

| Sección | Contenido |
|---|---|
| **Resumen** | Totales de endpoints, SP listos, SP migrados con hijos, SP llamados directamente, SP pendientes, advertencias, etc. |
| **1. SP por endpoint** | La tabla principal. Tiene **una fila por SP** y el endpoint se repite si usa varios. Columnas: *Endpoint*, *Package.SP llamado / migrado*, *SP hijo llamado dentro de la query*, *Tipo*, *Archivo:línea* (con enlace al archivo). |
| **2. SP listos** | SP migrados sin hijos, con los endpoints que los usan, el comentario que los identifica y la query que los reemplaza. Avisa con &#9888; si el SP **además** se sigue llamando directamente en otro endpoint. |
| **3. SP migrados con SP hijos** | Cada SP migrado junto con sus hijos y el estado de cada hijo: **pendiente**, o **ya migrado en el repo** (en ese caso se muestran también sus nietos). |
| **4. SP llamados directamente** | SP que se ejecutan tal cual, con la forma de llamada (nombre + `CommandType.StoredProcedure`, `BEGIN ... END;`, `CALL`/`EXEC`, `SELECT ... FROM DUAL` o dentro de una query). |
| **5. Inventario de pendientes** | Lista única de SP pendientes de migrar (llamados directamente + hijos no migrados), ordenada por la cantidad de endpoints que dependen de cada uno. Sirve para **priorizar**. |
| **6. Endpoints sin SP** | Endpoints que no usan SP, con el motivo (p. ej. "ejecuta 3 consultas SQL sin SP asociado"). |
| **7. Revisión manual** | SP mencionados solo en comentarios, advertencias y referencias a SP en código que ningún endpoint alcanza. Las advertencias son: "Llamada no resuelta", "SP dinámico", "Comando no resuelto", "Configuración ambigua", "Asociación ambigua" y "Profundidad máxima". |
| **8. Cruce con Swagger** | Solo aparece si usas `-SwaggerPath`. Muestra los endpoints documentados que no están en el código, y al revés. Como prefijo de las rutas usa `basePath` (Swagger 2) o la ruta de `servers[0].url` (OpenAPI 3). Si el archivo no se puede leer, el motivo queda escrito en esta sección. |
| **Metodología** | Carpetas omitidas, archivos de test excluidos y archivos leídos como ANSI (ver 6.6). |

### Valores de la columna *Tipo*

| Tipo | Significado |
|---|---|
| **Llamado directamente** | El flujo del endpoint ejecuta el SP. Debajo se indica la forma de la llamada. |
| **Llamado directamente (dentro de query)** | El SP o la función aparece dentro de un `SELECT`/`INSERT`/... que no tiene un comentario de SP migrado. |
| **Migrado sin hijos (listo)** | Un comentario nombra el SP y la query que lo reemplaza no llama a ningún SP. |
| **SP hijo (pendiente)** | La query que reemplazó al SP de la 2.ª columna todavía llama al SP de la 3.ª columna. |
| **SP hijo (ya migrado en el repo)** | Igual que el anterior, pero ese hijo ya tiene su propia query migrada en otro lugar del repo. |
| **SP hijo nivel N** | Nieto, bisnieto, etc. La 3.ª columna muestra la cadena: `HIJO → NIETO`. |
| **Solo en comentario (revisar)** | Se menciona un SP en el flujo del endpoint, pero no hay código que lo respalde. |
| **&dagger;** | La asociación es inferida: viene del comentario de un método llamador (p. ej. la acción del controller), o la llamada se resolvió solo por el nombre del método. Conviene verificarla. |

## 6. Cómo identifica cada caso

### 6.1 Endpoints

Detecta estos tipos de endpoint:

- **Controllers de ASP.NET Core**: `[ApiController]`, `[Route]` (también el heredado de un controller base), `[HttpGet("...")]`, `[AcceptVerbs]`, `[Area]`, `[ActionName]`, `[NonAction]`, rutas absolutas `~/` y tokens `[controller]`/`[action]`.
- **ASP.NET Web API 2** (.NET Framework): `[RoutePrefix]`/`[Route]`, y rutas convencionales tomadas de `MapHttpRoute`. En este caso el verbo se infiere del nombre del método.
- **Minimal APIs**: `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch`/`MapMethods`/`Map`. Los prefijos de `MapGroup` se combinan aunque el grupo se cree en `Program.cs` y se pase a métodos de extensión.
- **Carter**, **FastEndpoints** y **Azure Functions** (`HttpTrigger`).

La ruta se muestra como en Swagger: `GET /api/ventas/{id}`, sin las restricciones (`{id:int}` queda como `{id}`).

### 6.2 Recorrido del código

Desde cada endpoint se siguen las llamadas a:

- servicios y repositorios inyectados (campos, parámetros y constructores primarios);
- interfaces, hacia todas sus implementaciones, y clases base y overrides;
- handlers MediatR/CQRS (`_mediator.Send(new XQuery())` llega a `XQueryHandler.Handle`), `Publish` hacia los notification handlers y `ICommandHandler<T>`;
- métodos de extensión, clases estáticas (DAL) y métodos de un Unit of Work (`_uow.Ventas.Listar()`);
- clases base: la llamada `base.Metodo()` y los métodos virtuales se resuelven sobre el tipo concreto (template methods);
- `Lazy<T>.Value`, `IOptions<T>.Value`, casts `((IRepo)x).Metodo()`, diccionarios de estrategias (`_mapa[tipo].Ejecutar()`), fábricas `Func<T>` y `GetRequiredService<T>()`;
- grupos de métodos pasados como delegados (`ids.Select(_repo.Obtener)`) y funciones locales;
- propiedades con `get`/`set` (también `=>`), indexadores (`this[...]`), operadores y conversiones `implicit`/`explicit`;
- implementaciones explícitas de interfaz (`IVentasRepo.Listar()`), genéricos cerrados (`VentasRepo : RepoBase<Venta>`) y bloques `extension(...)` de C# 14;
- alias (`using Repo = Empresa.Datos.VentasRepo;`) y `global using`;
- constantes (`const string`, `static readonly`), también en clases de constantes como `StoredProcedures.ListarVentas`;
- strings interpolados con constantes (`$"{Paquete}.SP_X"`), `string.Format`, `string.Concat` y `AppendFormat`;
- queries armadas con `StringBuilder` o con `sql += ...`: las piezas se unen en una sola query, incluidos los comentarios SQL que vayan en su propia pieza;
- archivos `.sql` cargados por nombre, recursos `.resx` (también su `<comment>`) y valores de `appsettings*.json`, ya sea con `IConfiguration["Seccion:Clave"]`, `GetSection(...)[...]` o clases de opciones `IOptions<T>` (la sección se toma de `Configure<T>(GetSection("..."))`).

### 6.3 Nombres de SP

| Cómo aparece en el código | Cómo se reconoce |
|---|---|
| `PCK_VENTAS.SP_LISTAR`, `pck_ventas.sp_listar`, `"PCK_VENTAS"."SP_LISTAR"`, `ESQUEMA.PCK_VENTAS.SP_LISTAR`, `PCK_VENTAS.SP_LISTAR@DBLINK` | Todos se reportan como `PCK_VENTAS.SP_LISTAR`. El dblink se indica en el detalle. |
| `SP_X` / `FN_X` / `PRC_X` sin package | Dentro de SQL solo cuentan si se invocan: van seguidos de `(` o `;`, o después de `CALL`/`EXEC`. Así una columna llamada `SP_FLAG` no se confunde con un SP. |
| Dentro de un literal SQL `'...'` o `q'[...]'` | Se ignora: es un dato, no una llamada. |
| Dentro de un comentario SQL (`--` o `/* */`) | Es un **marcador**: documenta qué SP se migró. |
| En `logger.LogError(...)`, `throw new Exception(...)` o `Console.WriteLine` | No es una llamada. Solo sirve como pista débil: se usa únicamente si el método no tiene otro comentario de SP. |
| En comparaciones (`if (x == "PCK_...")`), valores de parámetros (`new { p = "PCK_..." }`, `parametros.Add(...)`) o respuestas HTTP (`Ok("...")`, `BadRequest("...")`) | Se ignora: no es un comando. |
| `PCK_X.SEQ.NEXTVAL`, `%TYPE`, `%ROWTYPE` | Se ignora: son secuencias o tipos, no SP. |

### 6.4 Asociación entre un SP migrado y su query (convención de comentarios)

El script busca el comentario que nombra al SP migrado empezando por el más cercano a la query:

1. Comentario SQL **dentro de la query**: `-- Migrado de PCK_VENTAS.SP_LISTAR_VENTAS`.
2. Comentario **sobre la constante** que tiene la query.
3. Comentario **previo dentro del mismo método**. Vale hasta el siguiente comentario que nombre otro SP y solo dentro de su bloque `{ }`: un comentario en la rama `if` no se aplica a la rama `else`. También cuenta un comentario al final de la misma línea.
4. XML doc (`/// <summary>`), comentarios (también al final de la línea de la firma) o atributos (p. ej. `[SwaggerOperation(Description = "SP: ...")]`) **del método**. Las menciones en mensajes de log solo se usan si no hay ninguno de los anteriores.
5. Documentación del **método de la interfaz** por la que se llamó, o comentario del **método llamador** más cercano, por ejemplo la acción del controller (&dagger;).
6. Comentario de la **clase**, solo si nombra exactamente un SP.

Si el comentario nombra un SP que la propia query **llama**, solo documenta esa llamada: no la convierte en migración.

También se acepta un comentario que nombra package y SP por separado, por ejemplo `// Package: PCK_VENTAS - SP: SP_LISTAR`.

**Recomendación para el equipo.** Para que el reporte sea exacto, deja el SP migrado en un comentario justo encima de la query (o dentro del SQL):

```csharp
// Migrado de PCK_VENTAS.SP_LISTAR_VENTAS
const string SqlListarVentas = @"
    SELECT v.ID, PCK_UTIL.FN_FORMATEAR_RUT(v.RUT) AS RUT   -- hijo pendiente
    FROM VENTAS v
    WHERE v.FECHA >= :desde";
```

### 6.5 Qué se excluye

- Las carpetas `bin`, `obj`, `.git`, `.vs`, `.vscode`, `.idea`, `node_modules`, `TestResults`, `.github`, `.gitlab`, `.azuredevops`, `.claude` y `.config`, en cualquier nivel.
- Las carpetas `packages` y `artifacts`, solo en la raíz de la carpeta analizada (más adentro pueden ser módulos de código).
- En las carpetas de documentación (`docs`, `doc`, `documentation`, `documentacion`, `documentación`) se ignoran los `.sql`, `.resx` y `appsettings*.json` (scripts y análisis). Los `.cs` sí se leen, porque puede haber un módulo de código llamado `Docs`. Si no quieres leerlos, exclúyelos con `-ExcludePath`.
- Los proyectos de test. Se detectan por el `.csproj` (`Microsoft.NET.Test.Sdk`, xUnit, NUnit, MSTest, `IsTestProject`) o por carpetas `test`/`tests`/`*.Tests`.
- Los archivos generados (`*.g.cs`, `*.Designer.cs`, `AssemblyInfo.cs`).
- Los `.sql`, `.md` y `.txt` de análisis: un `.sql` solo cuenta si el código C# lo referencia por nombre.
- Para excluir más carpetas, usa `-ExcludePath`. Da lo mismo si el patrón termina en `\` o `/`.

### 6.6 Lectura del código fuente

- **Codificación**: lee UTF-8 (con o sin BOM), UTF-16 y UTF-32. Si un archivo sin BOM no es UTF-8 válido, lo lee como Windows-1252 (ANSI), que es lo habitual en fuentes legacy de Visual Studio. Así se conservan los acentos de los comentarios. La sección *Metodología* lista los archivos leídos así.
- **Fines de línea**: acepta CRLF, LF y CR solo (archivos de Mac antiguos), por lo que los números de línea coinciden con los del editor.
- **Directivas `#if`/`#elif`/`#else`**: descarta las ramas que se sabe que son falsas, como `#if false`, `#if !true` o un símbolo con `#undef`. Si la condición depende de símbolos de compilación (`DEBUG`, `NET48`...), se analizan todas las ramas cuando eso deja el código bien formado. Si no, se analiza una sola, de preferencia la del `#else`. Un SP que solo aparece en una rama de `DEBUG` puede quedar en el reporte: revísalo en el archivo:línea indicado.
- **Nombres con caracteres Unicode** (acentos combinados, NFD): se tratan como parte del identificador.

## 7. Limitaciones conocidas

| Situación | Qué hace el script |
|---|---|
| El nombre del SP se arma en tiempo de ejecución (`"PCK_" + variable + ".SP_X"`), o el texto del comando llega en una variable que no se puede evaluar | No lo puede resolver. Lo deja como advertencia "SP dinámico" o "Comando no resuelto", y si un comentario nombra el SP, aparece como "Solo en comentario". |
| Una clase de opciones (`IOptions<T>`) cuya sección no se puede deducir y cuya clave aparece en varias secciones | Lo deja como advertencia "Configuración ambigua". |
| Sobrecargas con la misma cantidad de parámetros | Se siguen todas. Es una aproximación conservadora: puede sumar un SP de más, pero no omite ninguno. |
| Llamadas por reflexión, `dynamic` o delegados guardados en diccionarios | No se siguen. Si el método destino puede llegar a un SP, lo reporta como "Llamada no resuelta". |
| Un campo delegado al que el constructor le asigna una lambda (`_ejecutar = () => _repo.Listar();`) y que después se invoca (`_ejecutar()`) | No se sigue. El endpoint queda en "Endpoints sin SP" y, si ningún otro endpoint llega a `_repo.Listar`, su SP aparece en la sección 7.3 (referencias no vinculadas). |
| Ramas `#if` con símbolos de compilación | Ver 6.6: se pueden contar SP de una rama que no se compila en producción. |
| Varias clases con el mismo nombre de método y receptor de tipo desconocido | Si hay 3 clases o menos, sigue todas las candidatas y marca la fila con &dagger;. Si hay más, lo reporta como advertencia con los posibles destinos. |
| Sinónimos de Oracle o packages sin el prefijo `PCK_`/`PKG_` | Agrega el prefijo con `-PackagePrefixes`. Un nombre sin prefijo solo se detecta si se ejecuta con `CommandType.StoredProcedure`. |
| Lógica dentro de la BD (un SP pendiente que llama a otros SP) | El script solo ve el repo. Si un hijo está migrado en el repo, sí se siguen sus nietos. |

**Revisa siempre la sección 7 del reporte**: ahí está todo lo que el análisis no pudo afirmar con certeza.

## 8. Solución de problemas

| Problema | Solución |
|---|---|
| "No se puede cargar el archivo... la ejecución de scripts está deshabilitada" | Usa `powershell -ExecutionPolicy Bypass -File .\Analizar-SpEndpoints.ps1 ...` o `Unblock-File`. |
| "PowerShell está en modo ConstrainedLanguage" | Tu equipo restringe `Add-Type`. Ejecútalo desde una consola sin esa restricción o pide una excepción al área de TI. |
| Aparecen caracteres raros o errores de sintaxis después de editar el script | Guárdalo de nuevo como **UTF-8 con BOM**. |
| Un endpoint no aparece | Revisa que el controller o el método `Map*` no esté en una carpeta excluida. Con `-SwaggerPath` puedes ver qué endpoints documentados no se detectaron. |
| Un SP aparece como "Solo en comentario" | El nombre del SP no está escrito en el código (variable, configuración externa, etc.) o la query no se encontró. Revisa el archivo:línea indicado. |
| Quiero ver por qué un SP quedó asociado a un endpoint | Ejecuta con `-IncludeTrace` (árbol de métodos) o con `-ExportCsv` (columna `Traza`). |
| Advertencia "Profundidad máxima" | La cadena de llamadas de ese endpoint es más larga que `-MaxDepth`. Vuelve a ejecutar con un valor mayor, por ejemplo `-MaxDepth 80`. |
| Los prefijos o exclusiones no se aplican al usar `-File` | Pasa la lista separada por comas en un solo valor: `-PackagePrefixes PCK_,PKG_`. |
| La sección 8 dice que no se pudo leer el Swagger | Revisa la ruta o la URL. Con una URL `https://localhost`, la API debe estar levantada y el certificado de desarrollo debe ser de confianza. Si no, exporta el `swagger.json` a un archivo y pasa la ruta. |

## 9. Uso en un pipeline (opcional)

```yaml
# Azure DevOps (agente Windows)
- powershell: .\docs\local\scripts\Analizar-SpEndpoints.ps1 -RepoPath $(Build.SourcesDirectory)\src -OutputPath $(Build.ArtifactStagingDirectory)\sp-por-endpoint.md -ExportCsv
  displayName: Mapa de SP por endpoint
- publish: $(Build.ArtifactStagingDirectory)
  artifact: reporte-sp
```

## 10. Mantenimiento del script (para quien lo modifique)

El `.ps1` tiene tres partes:

1. **Parámetros y descubrimiento de archivos** (inicio del archivo): rutas por defecto, carpetas excluidas y detección de proyectos de test.
2. **Motor de análisis en C#** (dentro de `$engineSource = @' ... '@`): lexer, parser, grafo de llamadas, reglas de SP y clasificación. Es C# 5, para que compile con el `Add-Type` de Windows PowerShell 5.1.
3. **Render del reporte** (final del archivo): textos, tablas y CSV. Es la parte más fácil de personalizar.

Las fuentes separadas del motor, el script `build.ps1` que arma el `.ps1` y los repos de prueba con su resultado esperado están en la carpeta `_desarrollo/`. Ahí también está el comparador `compare.ps1`, que verifica que el script siga dando el resultado esperado después de un cambio:

```powershell
powershell -ExecutionPolicy Bypass -File .\_desarrollo\build.ps1
powershell -ExecutionPolicy Bypass -File .\_desarrollo\compare.ps1
```
