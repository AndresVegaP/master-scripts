# master-scripts

Herramienta para saber, por cada endpoint de una API .NET + Dapper, qué stored procedures de Oracle (`PCK_xxx.SP_xxx`) usa y en qué estado está su migración: llamados directamente, migrados a query (listos) o migrados que todavía llaman a otros SP (hijos pendientes).

## Contenido

| Archivo | Para qué sirve |
|---|---|
| [`Analizar-SpEndpoints.ps1`](Analizar-SpEndpoints.ps1) | El script. Es un solo archivo, sin dependencias, para Windows PowerShell 5.1 o PowerShell 7. |
| [`GUIA-Analizar-SpEndpoints.md`](GUIA-Analizar-SpEndpoints.md) | Guía de uso: parámetros, contenido del reporte, reglas de detección y limitaciones. |
| [`PROMPT-VERIFICACION-EMPRESA.md`](PROMPT-VERIFICACION-EMPRESA.md) | Prompt para que un asistente de IA en VS Code valide el script sobre el repo real y genere los reportes definitivos. |

## Uso

1. Descarga el ZIP (*Code > Download ZIP*) y extráelo. No hace falta git.
2. Ejecuta el script con `-ExecutionPolicy Bypass`, porque los archivos descargados de internet pueden venir bloqueados:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Analizar-SpEndpoints.ps1 -RepoPath C:\repos\mi-api -OutputPath .\reportes\sp-por-endpoint.md -ExportCsv
```

> Los reportes pueden contener información de la empresa: déjalos en `reportes/` y no los subas a este repositorio.
