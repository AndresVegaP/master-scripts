# Prompt: verificar `Analizar-SpEndpoints.ps1` sobre el repo de la empresa

Este archivo sirve para que un asistente de IA en VS Code (Claude Code, Copilot en modo agente o similar) haga cuatro cosas:

1. Entender qué es este proyecto.
2. Comprobar que el script funciona en el PC de la empresa.
3. Ejecutarlo sobre el repo de la API que necesitas analizar y validar el resultado contra el código real.
4. Corregir y terminar lo que haga falta, sin filtrar código de la empresa a este repositorio.

---

## 1. Preparación (la haces tú, una sola vez)

1. Clona este repositorio en el PC de la empresa:

```powershell
git clone https://github.com/AndresVegaP/master-scripts.git
```

2. Crea un workspace de VS Code con dos carpetas: este repo y el repo de la API que quieres analizar.
   - Copia [`analisis.code-workspace.example`](analisis.code-workspace.example) como `analisis.code-workspace`.
   - Ajusta las dos rutas.
   - Ábrelo con *Archivo > Abrir área de trabajo desde archivo...*

   El archivo `.code-workspace` está en `.gitignore`, así que no se sube.
3. Si tienes un `swagger.json` exportado de la API (o la API levantada en local), anota su ruta o URL: el asistente la puede usar para verificar los endpoints.
4. Abre el chat del asistente en modo agente y pega el prompt de la sección 2. Reemplaza los valores entre `<...>`.

---

## 2. Prompt para pegar

````text
Trabajas en un workspace de VS Code con DOS carpetas raíz:

- "master-scripts": mi herramienta. Contiene Analizar-SpEndpoints.ps1, un script de Windows PowerShell 5.1 con un motor de análisis estático en C# embebido. El script recorre un repo .NET + Dapper y, por cada endpoint (controllers, Minimal APIs, Carter, FastEndpoints, Azure Functions), lista los stored procedures de Oracle 10g que usa (packages PCK_xxx / PKG_xxx, procedimientos SP_xxx y funciones FN_xxx). Los clasifica como: llamado directamente, llamado dentro de una query, migrado sin hijos (listo), migrado que todavía llama SP hijos (pendientes), o mencionado solo en comentario.
- "<NOMBRE-CARPETA-REPO-API>": el repo de la API de la empresa que quiero analizar. La API usa Dapper con Oracle y documenta sus endpoints con OpenAPI 3.0/Swagger. Estamos migrando los SP de Oracle a queries dentro del repo. Normalmente se deja un comentario con el package y el SP migrado sobre la query o en el endpoint.

OBJETIVO: decidir si la herramienta me sirve tal como está sobre ESTE repo y, si no, corregirla y terminar lo pendiente. Al final quiero un veredicto claro y evidencia que lo respalde.

REGLAS (obligatorias)
1. NO modifiques nada dentro de "<NOMBRE-CARPETA-REPO-API>". Ese repo es de solo lectura para ti.
2. NO copies código, nombres de SP, rutas ni nombres de clases del repo de la empresa a ningún archivo versionado de master-scripts.
   - Todo lo que salga del análisis va en master-scripts/reportes/ (está en .gitignore).
   - Si necesitas reproducir un caso para corregir la herramienta, crea un fixture SINTÉTICO y ANONIMIZADO en master-scripts/_desarrollo/fixtures/, con nombres inventados.
3. No hagas git push ni crees PRs sin preguntarme. Puedes hacer commits locales en una rama nueva (por ejemplo "verificacion-empresa") en master-scripts, solo con cambios genéricos de la herramienta.
4. El script debe seguir funcionando en Windows PowerShell 5.1:
   - el motor (master-scripts/_desarrollo/cs/*.cs) es C# 5: sin interpolación $"", sin ?., sin nameof, sin => en miembros, sin tuplas, sin out var, sin pattern matching;
   - Analizar-SpEndpoints.ps1 se genera con _desarrollo/build.ps1 (UTF-8 con BOM). No lo edites a mano: edita las fuentes de _desarrollo/cs y _desarrollo/ps y reconstruye;
   - en las plantillas .ps1 no uses caracteres como → — “ ” Ó Ñ (con tildes minúsculas no hay problema).
5. Ante dudas sobre las convenciones del equipo, pregúntame antes de cambiar reglas de clasificación.

PASO 1. Entender el proyecto
- Lee master-scripts/README.md, GUIA-Analizar-SpEndpoints.md y _desarrollo/ESTADO.md. Revisa los pendientes que figuren ahí.
- Mira por encima la estructura de _desarrollo/:
  - cs/01..07: modelo, lexer, parser, índice/resolución de llamadas, SQL y marcadores, endpoints, análisis;
  - ps/: parámetros, descubrimiento de archivos y render;
  - build.ps1, compare.ps1 y fixtures/ con sus *.EXPECTED.md.
- Resúmeme en 10 líneas qué hace y cómo decide cada tipo (incluida la precedencia de comentarios para asociar un SP migrado con su query).

PASO 2. Verificar el entorno y la regresión
Ejecuta con Windows PowerShell 5.1 (powershell.exe, no pwsh), desde la carpeta master-scripts:
  powershell -NoProfile -ExecutionPolicy Bypass -Command "$PSVersionTable.PSVersion; $ExecutionContext.SessionState.LanguageMode; Get-ExecutionPolicy -List"
  powershell -NoProfile -ExecutionPolicy Bypass -File .\_desarrollo\build.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File .\_desarrollo\compare.ps1
- Lo esperado es "TOTAL esperadas N, faltan 0, sobran 0".
- Si el LanguageMode no es FullLanguage, o si Add-Type está bloqueado por AppLocker/antivirus, detente y explícame las opciones.
- Si algo falla aquí, arréglalo antes de seguir.

PASO 3. Reconocer el repo de la API (solo lectura)
Identifica y anota:
- versión de .NET y tipo de proyecto (ASP.NET Core o .NET Framework / Web API 2);
- estilo de endpoints (controllers, Minimal APIs con MapGroup, Carter, FastEndpoints, etc.);
- capas (controller -> service -> repository, MediatR/CQRS, Unit of Work, clases base);
- cómo se usa Dapper y Oracle: Oracle.ManagedDataAccess, Dapper.Oracle/OracleDynamicParameters, wrappers propios del tipo ExecuteSp(...), clases de constantes con nombres de SP, SQL en constantes, en .sql incrustados, en .resx o en appsettings;
- la convención REAL de comentarios para "SP migrado" (dónde y con qué texto se escribe);
- los prefijos reales de packages y SP. Búscalos con grep, sin distinguir mayúsculas, por ejemplo: "\b(PCK|PKG|PK)_\w+\.\w+" y "\b(SP|FN|PRC|F|P)_\w+\s*\(". Si hay otros prefijos, los usarás en -PackagePrefixes / -ObjectPrefixes;
- proyectos de test, carpetas de documentación o de scripts de BD que deban excluirse (-ExcludePath);
- si hay un swagger.json en el repo o una URL disponible: <RUTA-O-URL-SWAGGER o "no hay">.

PASO 4. Ejecutar la herramienta
  powershell -NoProfile -ExecutionPolicy Bypass -File .\Analizar-SpEndpoints.ps1 -RepoPath "<RUTA-ABSOLUTA-AL-REPO-API>\<subcarpeta src si aplica>" -OutputPath .\reportes\sp-por-endpoint.md -ExportCsv -IncludeTrace [-SwaggerPath <...>] [-PackagePrefixes ...] [-ExcludePath ...]
- Anota el tiempo y los totales de la consola.
- Si tarda más de 5 minutos o falla, investiga la causa (archivo problemático, rendimiento) y corrígela.

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
- crea un fixture sintético mínimo y anonimizado que lo reproduzca, con su .EXPECTED.md (mismo formato que los existentes);
- corrige en _desarrollo/cs o _desarrollo/ps;
- reconstruye con build.ps1;
- ejecuta compare.ps1: todo tiene que seguir en "faltan 0, sobran 0", incluido el fixture nuevo;
- vuelve a ejecutar sobre el repo de la API y confirma la mejora.
Si una corrección cambia una regla de clasificación, pregúntame antes.

PASO 7. Entregables
1. master-scripts/reportes/VERIFICACION.md (NO versionado) con:
   - un veredicto: "Sirve tal cual", "Sirve con ajustes (aplicados)" o "No sirve todavía", con la justificación;
   - las métricas: endpoints reales vs. detectados, filas muestreadas, filas correctas, % de precisión estimada y falsos negativos encontrados;
   - la lista de problemas con su clasificación y lo que se hizo con cada uno;
   - los pendientes que no se pudieron resolver;
   - recomendaciones de convención de comentarios para el equipo, para que el reporte sea exacto.
2. master-scripts/_desarrollo/ESTADO.md actualizado SOLO con información genérica de la herramienta (sin datos de la empresa).
3. Commits locales en la rama "verificacion-empresa" con los cambios de la herramienta. Antes de commitear, revisa el diff y confirma que no se filtra nada de la empresa.
4. Un resumen corto en el chat, con el veredicto y los próximos pasos.
````

---

## 3. Después de la verificación

- Abre `reportes/VERIFICACION.md` y `reportes/sp-por-endpoint.md` y revisa el veredicto.
- Si hubo correcciones en la herramienta, revisa el diff de la rama `verificacion-empresa`. Si todo está bien, súbela:

```powershell
git push -u origin verificacion-empresa
```

- Los reportes de la empresa (`reportes/`) se quedan solo en tu PC.
