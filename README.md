# master-scripts

Scripts de apoyo para el trabajo diario.

## Scripts

| Script | Para qué sirve | Documentación |
|---|---|---|
| [`Analizar-SpEndpoints.ps1`](Analizar-SpEndpoints.ps1) | Analiza un repo .NET + Dapper y genera un `.md` que indica, por cada endpoint, qué stored procedures de Oracle (`PCK_xxx.SP_xxx`) usa. Los clasifica en: llamados directamente, migrados a query (listos) y migrados que todavía llaman a otros SP (hijos pendientes). | [GUIA-Analizar-SpEndpoints.md](GUIA-Analizar-SpEndpoints.md) |

Uso rápido (Windows PowerShell 5.1 o PowerShell 7):

```powershell
.\Analizar-SpEndpoints.ps1 -RepoPath C:\repos\mi-api\src -OutputPath .\reportes\sp-por-endpoint.md -ExportCsv
```

## Verificarlo sobre otro repo con un asistente de IA

En [PROMPT-VERIFICACION-EMPRESA.md](PROMPT-VERIFICACION-EMPRESA.md) hay un prompt listo para pegar en un asistente de IA dentro de VS Code. El asistente analiza este proyecto junto con el repo que quieres revisar (en un mismo workspace), verifica si el script funciona sobre ese repo y corrige lo que haga falta.

## Desarrollo

- Las fuentes del motor y del script están en `_desarrollo/`.
- Los repos de prueba, con su resultado esperado, están en `_desarrollo/fixtures/`.
- El estado y los pendientes están en [`_desarrollo/ESTADO.md`](_desarrollo/ESTADO.md).

Para reconstruir el script y ejecutar las pruebas de regresión:

```powershell
powershell -ExecutionPolicy Bypass -File .\_desarrollo\build.ps1
powershell -ExecutionPolicy Bypass -File .\_desarrollo\compare.ps1
```

> Los reportes que se generen sobre repos de la empresa van en `reportes/`, que está en `.gitignore`. No se deben subir a este repositorio.
