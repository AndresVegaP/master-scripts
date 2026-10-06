# Prompt: verificar `Analizar-SpEndpoints.ps1` sobre el repo de la empresa

Prompt para pegar en el chat de un asistente de IA en VS Code (Claude Code, Copilot en modo agente o similar). Se usa con un workspace que ya tiene abiertas dos carpetas:

1. **La herramienta**: esta carpeta, descargada como ZIP desde GitHub (*Code > Download ZIP*) y extraída. Normalmente se llama `master-scripts-main`.
2. **El repo de la API de la empresa** que quieres analizar.

El asistente hace tres cosas:

- entiende qué es la herramienta;
- comprueba que funciona en el PC de la empresa;
- la ejecuta sobre la API, valida el resultado contra el código real y corrige lo que haga falta, sin sacar código de la empresa de tu PC.

> **Antes de abrir el workspace:** extrae el ZIP en una ruta corta, por ejemplo `C:\herramientas\`. Los repos de prueba de la herramienta tienen rutas profundas, y el Explorador de Windows falla con rutas de más de 260 caracteres. Si al extraer aparece "ruta demasiado larga", usa una carpeta más corta.

Reemplaza los valores entre `<...>` antes de enviarlo.

````text
Estás en un workspace de VS Code con dos carpetas raíz.

CARPETA 1, LA HERRAMIENTA. Es la raíz del workspace que contiene el archivo Analizar-SpEndpoints.ps1; probablemente se llama "master-scripts-main". La descargué como ZIP desde GitHub: NO es un repositorio git.
Analizar-SpEndpoints.ps1 es un script de Windows PowerShell 5.1 con un motor de análisis estático en C# embebido. Recorre un repo .NET + Dapper y, por cada endpoint (controllers, Minimal APIs, Carter, FastEndpoints, Azure Functions), lista los stored procedures de Oracle 10g que usa: packages PCK_xxx / PKG_xxx, procedimientos SP_xxx y funciones FN_xxx. Clasifica cada uno como:
- llamado directamente;
- llamado dentro de una query;
- migrado sin hijos (listo);
- migrado que todavía llama SP hijos (pendientes);
- mencionado solo en comentario.

CARPETA 2, EL REPO DE LA API. Es la otra raíz del workspace: el repo de la empresa que quiero analizar. Usa Dapper con Oracle y documenta sus endpoints con OpenAPI 3.0/Swagger. Estamos migrando los SP de Oracle a queries dentro del repo, y normalmente se deja un comentario con el package y el SP migrado sobre la query o en el endpoint.
Si el workspace tiene más de dos carpetas, o no está claro cuál es cuál, pregúntame antes de empezar.

OBJETIVO: decidir si la herramienta me sirve tal como está sobre ESTE repo y, si no, corregirla y terminar lo que falte. Al final quiero un veredicto claro y la evidencia que lo respalde.

REGLAS (obligatorias)
1. El repo de la API es de SOLO LECTURA: no crees, modifiques ni borres nada dentro de él.
2. No copies código, nombres de SP, rutas ni nombres de clases de la empresa a los archivos de la herramienta (cs/, ps/, fixtures/, documentación).
   - Todo lo que salga del análisis va en <herramienta>/reportes/.
   - Si necesitas reproducir un caso para corregir la herramienta, crea un fixture SINTÉTICO y ANONIMIZADO en <herramienta>/_desarrollo/fixtures/, con nombres inventados.
3. No uses git: no lo inicialices, no clones, no hagas commits ni push.
4. No descargues ni instales nada de internet: ni paquetes, ni módulos, ni herramientas. Usa solo lo que ya hay en el equipo.
5. Ejecuta siempre con Windows PowerShell 5.1 (powershell.exe, no pwsh) y con -NoProfile -ExecutionPolicy Bypass. Los archivos vienen de un ZIP descargado de internet y pueden estar marcados como bloqueados. Si aun así Windows los bloquea, propón "Get-ChildItem -Recurse '<carpeta herramienta>' | Unblock-File" y pídeme permiso antes de ejecutarlo.
6. El script debe seguir funcionando en Windows PowerShell 5.1:
   - el motor (_desarrollo/cs/*.cs) es C# 5: sin interpolación $"", sin ?., sin nameof, sin => en miembros, sin tuplas, sin out var, sin pattern matching;
   - Analizar-SpEndpoints.ps1 se genera con _desarrollo/build.ps1 (UTF-8 con BOM). No lo edites a mano: edita _desarrollo/cs o _desarrollo/ps y reconstruye;
   - en las plantillas .ps1 no uses caracteres como → — “ ” Ó Ñ (con tildes minúsculas no hay problema).
7. Si tienes dudas sobre las convenciones del equipo, o si una corrección cambia una regla de clasificación, pregúntame antes.

PASO 1. Entender la herramienta
- Lee README.md, GUIA-Analizar-SpEndpoints.md y _desarrollo/ESTADO.md (ahí están los pendientes conocidos).
- Mira por encima _desarrollo/:
  - cs/01..07: modelo, lexer, parser, índice y resolución de llamadas, SQL y marcadores, endpoints, análisis;
  - ps/: parámetros, descubrimiento de archivos y render;
  - build.ps1, compare.ps1 y fixtures/ con sus *.EXPECTED.md.
- Resúmeme en 10 líneas qué hace y cómo decide cada tipo, incluida la precedencia de comentarios para asociar un SP migrado con su query.

PASO 2. Verificar el entorno y la regresión
Desde la carpeta de la herramienta:
  powershell -NoProfile -ExecutionPolicy Bypass -Command "$PSVersionTable.PSVersion; $ExecutionContext.SessionState.LanguageMode; Get-ExecutionPolicy -List"
  powershell -NoProfile -ExecutionPolicy Bypass -File .\_desarrollo\build.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File .\_desarrollo\compare.ps1
- Lo esperado es "TOTAL esperadas N, faltan 0, sobran 0".
- Si el LanguageMode no es FullLanguage, o si Add-Type está bloqueado por AppLocker/antivirus, detente y explícame las opciones.
- Si faltan archivos de los fixtures (porque la extracción del ZIP falló por rutas largas), dímelo.
- Si algo falla aquí, arréglalo antes de seguir.

PASO 3. Reconocer el repo de la API (solo lectura)
Identifica y anota:
- versión de .NET y tipo de proyecto (ASP.NET Core o .NET Framework / Web API 2);
- estilo de endpoints;
- capas (controller -> service -> repository, MediatR/CQRS, Unit of Work, clases base);
- cómo se usa Dapper y Oracle: Oracle.ManagedDataAccess, Dapper.Oracle/OracleDynamicParameters, wrappers propios del tipo ExecuteSp(...), clases de constantes con nombres de SP, SQL en constantes, en .sql incrustados, en .resx o en appsettings;
- la convención REAL de comentarios para "SP migrado" (dónde y con qué texto se escribe);
- los prefijos reales de packages y SP. Búscalos sin distinguir mayúsculas, por ejemplo "\b(PCK|PKG|PK)_\w+\.\w+" y "\b(SP|FN|PRC|F|P)_\w+\s*\(". Si hay otros prefijos, los usarás en -PackagePrefixes / -ObjectPrefixes;
- proyectos de test, carpetas de documentación o de scripts de BD que deban excluirse (-ExcludePath);
- Swagger disponible: <RUTA-A-swagger.json o URL local, o "no hay">.

PASO 4. Ejecutar la herramienta
  powershell -NoProfile -ExecutionPolicy Bypass -File .\Analizar-SpEndpoints.ps1 -RepoPath "<RUTA-ABSOLUTA-AL-REPO-API o a su subcarpeta src>" -OutputPath .\reportes\sp-por-endpoint.md -ExportCsv -IncludeTrace [-SwaggerPath <...>] [-PackagePrefixes ...] [-ExcludePath ...]
- Anota el tiempo y los totales de la consola.
- Si tarda más de 5 minutos o falla, investiga la causa (archivo problemático, rendimiento, rutas largas) y corrígela.

PASO 5. Validar el resultado contra el código real
Haz un trabajo de auditor: no te quedes con lo que dice el reporte, verifícalo.
a) Endpoints
   - Compara la lista detectada con lo que existe realmente. Usa el Swagger si existe; si no, haz un grep de [Http*], [Route], Map*(.
   - Lista los faltantes y los sobrantes, con su causa.
b) Muestreo de filas
   - Por cada Tipo (Llamado directamente, Dentro de query, Migrado sin hijos, SP hijo, SP hijo nivel N, Solo en comentario), toma al menos 5 filas al azar, o todas si hay menos.
   - Para cada una: abre el archivo:línea, sigue la cadena de llamadas desde el endpoint (columna Traza del CSV o anexo del .md) y confirma que el SP, el hijo y el tipo son correctos.
   - Presta atención a las filas marcadas con † (asociación inferida).
c) Barrido de falsos negativos
   - Busca en todo el repo (excluyendo tests y docs) TODAS las referencias a SP con regex.
   - Cada una debe estar en: la tabla principal, la sección 7.3 (no vinculadas a endpoints) o un motivo justificado (comentario histórico, test, script de BD).
   - Lista las que falten.
d) Sección 7 del reporte
   - Clasifica cada advertencia, cada "solo en comentario" y cada referencia huérfana en una de estas categorías: (1) problema real del código de la API, (2) limitación o bug de la herramienta, o (3) correcto.
e) Convenciones no soportadas
   - Busca patrones del repo que la herramienta no entienda. Por ejemplo: otra redacción del comentario de migración, wrappers con otra firma, SP en enums/atributos/XML, EXECUTE IMMEDIATE, sinónimos sin prefijo, OracleCommand con CommandText armado en partes, inyección por convención o Scrutor, etc.

PASO 6. Corregir (solo si hace falta)
Por cada problema de la herramienta:
- crea un fixture sintético mínimo y anonimizado que lo reproduzca, con su .EXPECTED.md (mismo formato que los existentes) y agrégalo a la lista de compare.ps1;
- corrige en _desarrollo/cs o _desarrollo/ps;
- reconstruye con build.ps1;
- ejecuta compare.ps1: todo tiene que seguir en "faltan 0, sobran 0", incluido el fixture nuevo;
- vuelve a ejecutar sobre el repo de la API y confirma la mejora.

PASO 7. Entregables
1. <herramienta>/reportes/VERIFICACION.md (puede contener datos de la empresa, se queda en este PC) con:
   - un veredicto: "Sirve tal cual", "Sirve con ajustes (aplicados)" o "No sirve todavía", con la justificación;
   - las métricas: endpoints reales vs. detectados, filas muestreadas, filas correctas, % de precisión estimada y falsos negativos encontrados;
   - la lista de problemas con su clasificación y lo que se hizo con cada uno;
   - los pendientes que no se pudieron resolver;
   - recomendaciones de convención de comentarios para el equipo, para que el reporte sea exacto.
2. Si modificaste la herramienta:
   - actualiza _desarrollo/ESTADO.md SOLO con información genérica (sin datos de la empresa);
   - crea <herramienta>/cambios-herramienta/ con una copia de CADA archivo de la herramienta que creaste o modificaste, respetando su ruta relativa (por ejemplo cambios-herramienta/_desarrollo/cs/05-sql.cs). Incluye el Analizar-SpEndpoints.ps1 reconstruido y los fixtures nuevos;
   - agrega cambios-herramienta/CAMBIOS.md con la lista de archivos y qué se cambió en cada uno, explicado de forma genérica;
   - revisa archivo por archivo que esa carpeta NO contiene nada de la empresa (código, nombres de SP, rutas, clases, URLs). Ningún archivo de reportes/ debe ir ahí.
3. Un resumen corto en el chat con el veredicto, los próximos pasos y, si aplica, la lista de archivos de cambios-herramienta/.
````

---

## Después de la verificación

- Revisa `reportes/VERIFICACION.md` y `reportes/sp-por-endpoint.md`. **No los subas a ningún lado:** pueden contener información de la empresa.
- Si se creó la carpeta `cambios-herramienta/`, revisa sus archivos y súbelos al repo de GitHub desde la web: en la página del repo, *Add file > Upload files*. Arrastra las carpetas manteniendo la estructura. Otra opción es pasárselos a Claude para que los integre en la próxima sesión.
