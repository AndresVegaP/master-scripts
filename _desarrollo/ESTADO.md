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
- `adversarial/`: casos creados por la revisión adversarial.

## Estado
- Fixtures: 108/108 filas esperadas coinciden en endpoint, SP, hijo, tipo y archivo:línea. También coinciden los endpoints sin SP y las referencias huérfanas.
- Repo original de clean-architecture (sin SP): detecta 9 endpoints y 0 SP.

## Ciclo de cambio
```powershell
powershell -ExecutionPolicy Bypass -File .\_desarrollo\build.ps1
powershell -ExecutionPolicy Bypass -File .\_desarrollo\compare.ps1
```

## Pendientes
- **Revisión adversarial**: 5 agentes la ejecutan en paralelo, cada uno con una dimensión: parser C#, rutas, grafo de llamadas, semántica de clasificación, y comportamiento PS 5.1 / escala. Los hallazgos confirmados se incorporan en commits posteriores. Antes de usar el script, revisa el historial de commits.
- **Validación sobre un repo real de la empresa**: guiada por `PROMPT-VERIFICACION-EMPRESA.md`.
- **Idea opcional, no implementada**: un parámetro `-OracleSourcePath` para leer el fuente de los packages (`.pkb`/`.pks`) y mostrar qué SP llaman, dentro de la BD, los SP pendientes.

## Restricciones de mantenimiento
- El motor debe ser **C# 5** para que compile con `Add-Type` en Windows PowerShell 5.1. No usar `$""`, `?.`, `nameof`, miembros con `=>`, tuplas ni `out var`.
- `Analizar-SpEndpoints.ps1` se genera con `build.ps1` en UTF-8 con BOM. No editarlo a mano.
- En las plantillas `.ps1`, evitar `→`, `—`, comillas tipográficas, `Ó` y `Ñ`. Si alguien guarda el archivo sin BOM, PS 5.1 lo lee como ANSI y esos bytes rompen el parseo.
