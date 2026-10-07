# Estado del desarrollo: Analizar-SpEndpoints.ps1

## Estructura de `_desarrollo/`
- `cs/01..07-*.cs`: motor C# 5. El namespace `SPA_NS` se reemplaza por un hash al compilar.
  - `01-model`: modelo de datos y utilidades.
  - `02-lexer`: lexer de C#.
  - `03-parser`: parser estructural.
  - `04-index`: índice de tipos, resolución de llamadas y constantes.
  - `05-sql`: nombres de SP, escaneo SQL, fragmentos y marcadores por método.
  - `06-endpoints`: controllers, Web API 2, Minimal APIs, Carter, FastEndpoints y Azure Functions.
  - `07-analysis`: decisión de tipo, recorrido por endpoint, filas, catálogo de migrados, huérfanos, resx y appsettings.
- `ps/template-head.ps1`: ayuda, parámetros, descubrimiento de archivos y exclusiones.
- `ps/template-tail.ps1`: compilación del motor y agregados.
- `ps/template-render.ps1`: render del `.md`, CSV y cruce con Swagger.
- `build.ps1`: arma `../Analizar-SpEndpoints.ps1` (UTF-8 con BOM).
- `compare.ps1`: compara el CSV generado con la tabla "Filas esperadas" de cada `fixtures/*.EXPECTED.md`.
- `fixtures/`: 4 repos sintéticos con su verdad esperada:
  - A: MVC.
  - B: MediatR + Minimal APIs + Carter + FastEndpoints.
  - C: Web API 2 legacy.
  - D: copia de clean-architecture con SP.
- `adversarial/`: casos creados por la revisión adversarial. Está en `.gitignore`: queda solo en el equipo de desarrollo.
- `quickcase.ps1`: ejecuta el script sobre una carpeta y muestra las filas del CSV, los endpoints sin SP y las advertencias.
- `compile-test.ps1`: compila solo el motor C# para ver los errores con número de línea.

## Estado
- Fixtures: 108/108 filas esperadas coinciden en endpoint, SP, hijo, tipo y archivo:línea. También coinciden los endpoints sin SP y las referencias huérfanas.
- Repo original de clean-architecture (sin SP): detecta 9 endpoints y 0 SP.
- **Revisión adversarial, ronda 1**: 55 hallazgos. Se corrigieron y verificaron unos 50. Las sobrecargas con la misma cantidad de parámetros se aceptan como aproximación conservadora: se siguen todas.
- **Revisión adversarial, ronda 2** (5 dimensiones; la carpeta `adversarial/` acumula 207 casos de ambas rondas). Se corrigió lo siguiente:
  - Lectura: preprocesador `#if`/`#elif`/`#else`, archivos ANSI (Windows-1252), CR solo, identificadores NFD y comentarios dentro de huecos interpolados.
  - Parser: propiedades, indexadores, operadores, implementaciones explícitas de interfaz, alias, `global using`, bloques `extension(...)` y rutas interpoladas con constantes.
  - Grafo de llamadas: genéricos cerrados (sustitución en la base), `new X.Query()`, funciones locales y reasignación de variables.
  - Reporte: CSV con encabezado aunque no haya filas, Swagger con claves duplicadas y `servers`, escape HTML, `-OutputPath` distinto de `.md`, parámetros de lista con `-File`, `-ExcludePath` con separador final, carpetas `docs`/`packages`/`artifacts` y aviso de `-MaxDepth`.
- **Limitación aceptada**: un campo delegado al que el constructor le asigna una lambda no se sigue (`adversarial/parser/case18`). Está documentada en la guía.

## Ciclo de cambio
```powershell
powershell -ExecutionPolicy Bypass -File .\_desarrollo\build.ps1
powershell -ExecutionPolicy Bypass -File .\_desarrollo\compare.ps1
```

## Pendientes
- **Validación sobre un repo real de la empresa**: guiada por `PROMPT-VERIFICACION-EMPRESA.md`.
- **Idea opcional, no implementada**: un parámetro `-OracleSourcePath` para leer el fuente de los packages (`.pkb`/`.pks`) y mostrar qué SP llaman, dentro de la BD, los SP pendientes.

## Restricciones de mantenimiento
- El motor debe ser **C# 5** para que compile con `Add-Type` en Windows PowerShell 5.1. No usar `$""`, `?.`, `nameof`, miembros con `=>`, tuplas ni `out var`.
- `Analizar-SpEndpoints.ps1` se genera con `build.ps1` en UTF-8 con BOM. No editarlo a mano.
- En las plantillas `.ps1`, evitar `→`, `—`, comillas tipográficas, `Ó` y `Ñ`. Si alguien guarda el archivo sin BOM, PS 5.1 lo lee como ANSI y esos bytes rompen el parseo.
