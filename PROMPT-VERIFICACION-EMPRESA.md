# Prompt: validar `Analizar-SpEndpoints.ps1` sobre el repo real y generar los reportes

Prompt para pegar en el chat de un asistente de IA en VS Code (Claude Code, Copilot en modo agente o similar). El workspace ya debe estar abierto con dos carpetas:

1. **La herramienta**: el ZIP de este repo, descargado desde GitHub (*Code > Download ZIP*) y extraído. Normalmente se llama `master-scripts-main`.
2. **El repo de la API** que quieres analizar.

El asistente comprueba que la herramienta funciona en tu PC y que es compatible con el repo real. Después la ejecuta y audita el resultado contra el código. Solo la corrige si algo falla **en ese repo**. Al final deja los reportes definitivos.

El prompt no sirve para seguir desarrollando la herramienta: el desarrollo se hace fuera de la empresa.

Antes de enviarlo, reemplaza los valores entre `<...>`.

````text
Estás en un workspace de VS Code con dos carpetas raíz. Trabaja solo con lo que hay en ellas.

CARPETA 1, LA HERRAMIENTA. Es la raíz que contiene Analizar-SpEndpoints.ps1 (probablemente "master-scripts-main"). La extraje de un ZIP: NO es un repositorio git. Trae solo estos archivos:
- Analizar-SpEndpoints.ps1: script de Windows PowerShell 5.1 con un motor de análisis estático en C# embebido. Recorre un repo .NET + Dapper y, por cada endpoint (controllers, Web API 2, Minimal APIs, Carter, FastEndpoints, Azure Functions), lista los stored procedures de Oracle 10g que usa: packages PCK_/PKG_, procedimientos SP_ y funciones FN_. Clasifica cada uno como llamado directamente, llamado dentro de una query, migrado sin hijos (listo), migrado que todavía llama SP hijos (pendientes) o mencionado solo en comentario.
- GUIA-Analizar-SpEndpoints.md: parámetros, contenido del reporte, códigos del CSV, reglas de detección y limitaciones conocidas.
- README.md y PROMPT-VERIFICACION-EMPRESA.md (este prompt).
La herramienta ya se desarrolló y se probó fuera de la empresa. Aquí NO se sigue desarrollando: solo se valida sobre este repo, se corrige lo que este repo necesite y se generan los reportes reales.

CARPETA 2, EL REPO DE LA API. Es la otra raíz: el repo de la empresa que quiero analizar. Usa Dapper con Oracle y documenta sus endpoints con OpenAPI 3.0/Swagger. Estamos migrando los SP de Oracle a queries dentro del repo, y normalmente se deja un comentario con el package y el SP migrado sobre la query o en el endpoint.
Si el workspace tiene más de dos carpetas, o no está claro cuál es cuál, pregúntame antes de empezar.

OBJETIVO
1. Confirmar que la herramienta funciona en este PC y que es compatible con este repo.
2. Auditar su resultado contra el código real.
3. Corregir la herramienta SOLO si un fallo afecta el resultado sobre este repo.
4. Dejar los reportes definitivos en <herramienta>/reportes/, con un veredicto y la evidencia que lo respalde.
No agregues funciones que este repo no necesite, no refactorices y no "mejores" nada que no cambie el resultado sobre este repo.

REGLAS (obligatorias)
1. El repo de la API es de SOLO LECTURA: no crees, modifiques ni borres nada en él. Tampoco lo compiles (generaría bin/ y obj/).
2. Todo lo que contenga datos de la empresa (código, nombres de SP, rutas, nombres de clases, URLs) va SOLO en <herramienta>/reportes/. Nunca en Analizar-SpEndpoints.ps1, en la guía, en el README ni en <herramienta>/cambios-herramienta/.
3. No uses git: no lo inicialices, no clones, no hagas commits ni push.
4. No descargues ni instales nada: ni paquetes, ni módulos, ni herramientas, ni extensiones. Usa solo lo que ya hay en el equipo. La única conexión permitida es a la URL local del Swagger, si te la doy.
5. Ejecuta los comandos desde la carpeta de la herramienta y siempre con Windows PowerShell 5.1: powershell.exe (no pwsh) con -NoProfile -ExecutionPolicy Bypass -File.
   - Los comandos que empiezan con "powershell ... -File" usan rutas con "/" y funcionan igual desde PowerShell, cmd o bash.
   - Todo lo demás (los bloques de este prompt, el comando del PASO 6.3 y el Unblock-File) es código de PowerShell, probado en Windows PowerShell 5.1. Si tu terminal no es PowerShell (por ejemplo, Git Bash), guárdalo en reportes/scripts/<nombre>.ps1 como UTF-8 CON BOM y ejecútalo con powershell -NoProfile -ExecutionPolicy Bypass -File. Sin BOM, PowerShell 5.1 lee mal las rutas con tildes o ñ.
   - Si un bloque dice que no encuentra la ruta del repo, su resultado vacío NO vale como "no aplica": corrige la ruta y repítelo.
   - Si Windows bloquea los archivos del ZIP, propón "Get-ChildItem -Recurse '<carpeta herramienta>' | Unblock-File" y pídeme permiso antes de ejecutarlo.
6. Pregúntame antes de:
   - cambiar una regla de clasificación (cuándo un SP es directo, migrado, hijo o solo comentario, o la precedencia de comentarios);
   - suponer una convención del equipo que el código no deja clara;
   - hacer en el motor un cambio que no sea pequeño y localizado.
7. Toda corrección a la herramienta sigue el protocolo del PASO 6. No hagas cambios "de paso".

PASO 1. Entender la herramienta
- Lee GUIA-Analizar-SpEndpoints.md completa. Lo clave está en tres secciones:
  - 5: el reporte, la columna Tipo y los códigos del CSV;
  - 6: cómo identifica cada caso, incluida la precedencia de comentarios para asociar un SP migrado con su query;
  - 7: limitaciones conocidas.
- Ubica en Analizar-SpEndpoints.ps1 sus tres partes (sección 10 de la guía). No leas el motor completo: solo la parte que haga falta si hay que corregir algo.
- Resúmeme en 10 líneas qué hace la herramienta y cómo decide cada Tipo.

PASO 2. Reconocer el repo de la API (solo lectura)
Identifica y anota:
- versión de .NET y tipo de proyecto (ASP.NET Core o .NET Framework / Web API 2);
- los proyectos de API (los que exponen endpoints) y TODOS los proyectos que referencian (<ProjectReference> en los .csproj, también los transitivos).
  - La carpeta que se pase en -RepoPath debe contenerlos a todos; normalmente es la raíz del repo. Escríbela absoluta, con "/" y sin barra final.
  - Si hay más de una API, dime cuáles son y pregúntame si quiero un reporte por API o uno solo.
- el estilo de endpoints y las capas: controller -> service -> repository, MediatR/CQRS, Unit of Work, clases base;
- cómo se usa Dapper con Oracle:
  - Oracle.ManagedDataAccess, Dapper.Oracle u OracleDynamicParameters;
  - wrappers propios, del tipo ExecuteSp(...);
  - clases de constantes con nombres de SP;
  - SQL en constantes, en .sql incrustados, en .resx o en appsettings;
- la convención REAL de comentarios para "SP migrado": dónde se escribe y con qué texto;
- los prefijos reales de packages y SP. Descúbrelos con este bloque, que cuenta los prefijos que aparecen en el repo:
    $repo = '<RUTA-DEL-REPO-API>'
    $f = Get-ChildItem $repo -Recurse -File -Include *.cs,*.sql,*.resx,*.json,*.config,*.xml | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    'Pares PAQUETE_ . OBJETO_:'
    $f | Select-String -AllMatches -Pattern '\b([A-Z][A-Z0-9]*)_\w+[\\"]*\.[\\"]*([A-Z][A-Z0-9]*)_\w+' | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value.ToUpper() + '_ . ' + $_.Groups[2].Value.ToUpper() + '_' } | Group-Object | Sort-Object Count -Descending | Format-Table Count, Name -AutoSize
    'Objetos invocados sin package, PREFIJO_xxx(:'
    $f | Select-String -AllMatches -CaseSensitive -Pattern '(?<![\w.@])([A-Z][A-Z0-9]*)_\w+\s*\(' | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value + '_' } | Group-Object | Sort-Object Count -Descending | Format-Table Count, Name -AutoSize
  - Cada prefijo de package u objeto que no esté en los valores por defecto (PCK_, PKG_ / SP_, FN_, PRC_) se agrega en -PackagePrefixes / -ObjectPrefixes.
  - OJO: la lista REEMPLAZA a los valores por defecto. Incluye también los que se usen, por ejemplo -PackagePrefixes PCK_,PKG_,PK_ -ObjectPrefixes SP_,FN_,PRC_,F_.
  - Un par del tipo ESQUEMA_ . PCK_ es un esquema, no un prefijo nuevo.
  - En la segunda lista, ignora las funciones de Oracle (TO_CHAR, TO_DATE, SYS_...), los nombres de db link (lo que va después de @) y los nombres de métodos de C#.
  - Las listas se escriben separadas por comas y SIN espacios: -PackagePrefixes PCK_,PKG_,PK_ (con espacios, el script se detiene con "No se encuentra ningún parámetro de posición...").
- los proyectos de test, las carpetas de documentación y los scripts de BD que haya que excluir (-ExcludePath);
- Swagger disponible: <RUTA-A-swagger.json o URL local, o "no hay">.

PASO 3. Primera ejecución (línea base)
  powershell -NoProfile -ExecutionPolicy Bypass -File ./Analizar-SpEndpoints.ps1 -RepoPath "<RUTA-DEL-REPO-API>" -OutputPath ./reportes/base/sp-por-endpoint.md -ExportCsv -IncludeTrace [-SwaggerPath <...>] [-PackagePrefixes ...] [-ObjectPrefixes ...] [-ExcludePath ...]
Revisa la consola y el inicio del reporte:
- La consola debe mostrar "Analizar-SpEndpoints v1.1.0" y "PowerShell  : 5.1... Desktop (FullLanguage)".
  - Si en su lugar aparece "PowerShell esta en modo ...", el modo no es FullLanguage. Detente y explícame las opciones.
  - Si aparece "No se pudo compilar el motor de analisis: ...", probablemente AppLocker o el antivirus bloquean Add-Type. Detente y explícame las opciones.
- La línea "Salida" debe apuntar a reportes/base/ dentro de la herramienta.
- La cantidad de archivos .cs debe ser coherente con el repo.
- No debe aparecer el aviso de proyectos referenciados fuera de -RepoPath, ni "Análisis incompleto" al inicio del .md. Si aparece, amplía -RepoPath y vuelve a ejecutar.
- La línea "Prefijos de package: ..." del inicio del .md debe tener exactamente los prefijos que decidiste en el PASO 2.
- Anota el tiempo. Si tarda más de 5 minutos o falla, investiga la causa (un archivo problemático, rendimiento, rutas) antes de seguir.

PASO 4. Auditar el resultado contra el código real
Haz un trabajo de auditor: no te quedes con lo que dice el reporte, verifícalo.
a) Endpoints
   - Compara los endpoints detectados con los que existen realmente. Usa el Swagger si lo hay (sección 8 del reporte).
   - Si no hay Swagger, búscalos según los estilos que encontraste en el PASO 2:
     - [Http*], [Route] y [AcceptVerbs] (controllers);
     - MapGet/MapPost/MapPut/MapDelete/MapPatch/MapMethods/Map( y sus MapGroup (Minimal API y Carter);
     - Get(/Post(/Put(/Delete(/Patch( dentro de Configure() en clases que heredan de Endpoint<...> (FastEndpoints);
     - [Function]/[FunctionName] con HttpTrigger (Azure Functions);
     - MapHttpRoute (rutas convencionales de Web API 2).
   - No cuentes como endpoints los Map* que solo registran módulos (MapCarter, MapControllers, extensiones propias).
   - Lista los faltantes y los sobrantes, con su causa.
b) Muestreo de filas
   - Por cada Tipo, toma al menos 5 filas al azar, o todas si hay menos. En el CSV, los códigos están en la sección 5 de la guía: DIRECTO, DIRECTO_EN_QUERY, MIGRADO_LISTO, HIJO con Nivel=1 o Nivel>1, y SOLO_COMENTARIO. Las filas † tienen Inferido=True.
   - En cada fila, abre el archivo:línea y sigue la cadena de llamadas desde el endpoint. Usa la columna Traza del CSV o el anexo del .md. Confirma que el SP, el hijo, el estado del hijo y el tipo son correctos.
   - Revisa con más cuidado las filas con †.
c) Barrido de falsos negativos
   - Lista todas las referencias a SP del repo con este bloque. Ajusta los prefijos a los que encontraste en el PASO 2:
       $repo = '<RUTA-DEL-REPO-API>'
       Get-ChildItem $repo -Recurse -File -Include *.cs,*.sql,*.resx,appsettings*.json | Where-Object { $r = $_.FullName.Substring($repo.Length); $r -notmatch '(?i)\\(bin|obj|tests?|[^\\]*\.Tests)\\' -and ($_.Extension -eq '.cs' -or $r -notmatch '(?i)\\(docs?|documentation|documentaci.n)\\') } | Select-String -Pattern '\b(PCK|PKG|PK|SP|FN|PRC)_\w+', '\b(F|P)_\w+[\\"]*\s*\(' | ForEach-Object { '{0}:{1}: {2}' -f $_.Path, $_.LineNumber, $_.Line.Trim() }
   - Cada referencia debe estar en uno de estos lugares, o tener un motivo justificado:
     - la tabla principal;
     - la sección 7.3 (referencias no vinculadas a ningún endpoint), pero solo si confirmaste que ningún endpoint llega a ese código. Si P1 o L3 muestran que un endpoint sí llega, es un falso negativo;
     - motivos justificados:
       - comentario histórico, test o script de BD;
       - lo que la herramienta ignora a propósito (secciones 6.3 y 6.5 de la guía): un literal de datos dentro del SQL, un mensaje de log o de excepción, una constante que solo nombra el package, un .sql que el código no referencia por nombre;
       - una carpeta que la herramienta excluye (bin, obj, .vs, node_modules; ver la Metodología del reporte).
   - Lista las que falten. Son los falsos negativos de la métrica del PASO 7.
d) Sección 7 del reporte
   - Clasifica cada advertencia, cada "solo en comentario" y cada referencia no vinculada según si la herramienta acertó:
     - (3) correcto: aunque sea código muerto o un job sin registrar (anótalo como observación para el equipo);
     - (2) limitación o fallo de la herramienta;
     - (1) problema real del código de la API: solo si hay un error real (por ejemplo, llama a un SP que no existe o el comentario de migración nombra un SP equivocado).
   - Si existe la sección 7.4 (archivos que no se pudieron leer), cada archivo listado es una fuente de falsos negativos: revisa sus SP a mano.
e) Convenciones no soportadas
   - Busca patrones del repo que la herramienta no entienda. Por ejemplo:
     - otra redacción del comentario de migración;
     - wrappers con otra firma;
     - SP en enums, atributos o XML;
     - EXECUTE IMMEDIATE;
     - sinónimos sin prefijo;
     - OracleCommand con el CommandText armado en partes;
     - inyección por convención o con Scrutor.
f) Pendiente y limitaciones conocidas: revisa la lista de abajo. Para cada punto, decide si APLICA a este repo y anota la evidencia (qué buscaste y qué encontraste). Si no aplica, márcalo "no aplica" y no hagas nada más con él.

PENDIENTE CONOCIDO DE LA HERRAMIENTA (revisar solo si aplica)
P1. Campo o propiedad delegado al que se le asigna una lambda o un método. Por ejemplo, en el constructor: _ejecutar = () => _repo.Listar(); y después, en un método: _ejecutar() o _ejecutar.Invoke().
    - Qué hace hoy la herramienta: no sigue esa invocación. El endpoint queda en "Endpoints sin SP" (o le faltan SP) y, si ningún otro endpoint llega al método de la lambda, su SP aparece en la sección 7.3.
    - Las lambdas en variables locales SÍ se siguen, y también las fábricas Func<T> inyectadas.
    - Cómo detectarlo:
      1. Descarte rápido: busca "\b(Func|Action)\b" y "\bdelegate\b" en los .cs (sin bin/obj). Si no hay coincidencias, P1 no aplica.
      2. Si hay coincidencias, ejecuta este bloque. Lista las declaraciones de campos y propiedades Func/Action (también con ? y con el tipo partido en varias líneas) y de los delegate propios, y después todos los usos de cada nombre:
           $repo = '<RUTA-DEL-REPO-API>'
           $cs = @(Get-ChildItem -Path $repo -Recurse -File -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
           $decl = @($cs | Select-String -CaseSensitive -Context 0,2 -Pattern '\b(Func|Action)\b(\s*<[^;{]*>)?\??\s+_?\w+\s*(;|=|\{)', '\b(Func|Action)\s*<[^;{>]*$')
           $tipos = @($cs | Select-String -CaseSensitive -Pattern '\bdelegate\s+[^=;(]+\s(\w+)\s*[<(]' | ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique)
           if ($tipos.Count) { $decl += @($cs | Select-String -CaseSensitive -Context 0,2 -Pattern ('\b(' + ($tipos -join '|') + ')\??\s+_?\w+\s*(;|=|\{)')) }
           $decl | ForEach-Object { '{0}:{1}: {2}' -f $_.Path, $_.LineNumber, $_.Line.Trim() }
           $nombres = @($decl | ForEach-Object { if ($_.Matches[0].Value -match '(_?\w+)\s*(;|=|\{)$') { $Matches[1] } elseif ((@($_.Line) + @($_.Context.PostContext) -join ' ') -match '>\s*\??\s+(_?\w+)\s*(;|=|\{)') { $Matches[1] } } | Sort-Object -Unique)
           foreach ($n in $nombres) { "--- $n"; $cs | Select-String -CaseSensitive -Pattern "\b$n\b" -Context 0,1 | ForEach-Object { '{0}:{1}: {2}' -f $_.Path, $_.LineNumber, $_.Line.Trim(); if ($_.Line -match '=\s*$') { '      (sigue) ' + ($_.Context.PostContext -join ' ').Trim() } } }
      3. Para cada nombre, revisa sus usos:
         - Asignación: una lambda (x =>, (a, b) =>, async, static), un método (_repo.Listar), delegate (...) { } o new Func<...>(...), en el constructor, en la declaración o en { get; } = ... Si la línea termina en "=", mira la siguiente.
         - Invocación: nombre(...), nombre.Invoke(...) o nombre?.Invoke(...).
         - Descarta las variables locales: esas sí se siguen.
      4. Solo APLICA si alguna de esas lambdas o métodos llega a acceso a datos (un repositorio, Dapper, una conexión o un SP) y se invoca desde el flujo de algún endpoint.
    - Si aplica:
      - lista los endpoints afectados y los SP que les faltan;
      - agrégalos en "Asociaciones manuales" (PASO 7), con Acción = agregar y el tipo que les correspondería según las reglas de la guía;
      - pregúntame si quieres que se corrija la herramienta aquí. Es un cambio en el grafo de llamadas del motor. Pista técnica: el motor modela las funciones locales como métodos sintéticos (busca FindLocalFunctions y LocalFunctionsInScope). Lo equivalente sería crear un método sintético por cada lambda asignada a un campo delegado del tipo (con el nombre del campo y la lambda como cuerpo) y resolver _campo(...) y _campo.Invoke(...) hacia él.
      - Corrijas o no, describe el patrón en cambios-herramienta/CAMBIOS.md, en "Pendientes para desarrollo" (ver PASO 5).

LIMITACIONES CONOCIDAS (revisar solo si aplican; detalle en la sección 7 de la guía)
L1. Ramas #if con símbolos de compilación (DEBUG, NET48...).
    - Detección: busca "^\s*#\s*if\b" en los .cs (sin bin/obj). Si no hay coincidencias, no aplica.
    - Si hay, cruza cada bloque #if...#endif con las filas del CSV que caen dentro:
        $repo = '<RUTA-DEL-REPO-API>'; $csv = @(Import-Csv ./reportes/base/sp-por-endpoint.csv -Delimiter ';')
        $base = (Resolve-Path $repo).Path.TrimEnd('\')
        Get-ChildItem $base -Recurse -File -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | ForEach-Object {
            $rel = $_.FullName.Substring($base.Length + 1).Replace('\', '/'); $pila = New-Object System.Collections.Stack; $n = 0
            foreach ($l in [IO.File]::ReadAllLines($_.FullName)) {
                $n++
                if ($l -match '^\s*#\s*if\b') { $pila.Push(@($n, $l.Trim())) }
                elseif ($l -match '^\s*#\s*endif\b' -and $pila.Count) {
                    $b = $pila.Pop(); $ini = $b[0]; '{0}:{1}-{2}  {3}' -f $rel, $ini, $n, $b[1]
                    $csv | Where-Object { ($_.Archivo -eq $rel -and [int]$_.Linea -gt $ini -and [int]$_.Linea -lt $n) -or ($_.QueryArchivo -eq $rel -and $_.QueryLinea -and [int]$_.QueryLinea -gt $ini -and [int]$_.QueryLinea -lt $n) } | ForEach-Object { '    fila: {0} {1} | {2} {3} | {4} | linea {5}' -f $_.Verbo, $_.Ruta, $_.SP, $_.SpHijo, $_.Tipo, $_.Linea }
                }
            }
        }
    - Aplica solo si algún bloque tiene filas. Para saber qué rama se compila en producción, busca <DefineConstants> en los .csproj y en Directory.Build.props, y la configuración del pipeline si está en el repo. Si no queda claro, pregúntame (normalmente es Release, sin DEBUG).
    - No modifiques el reporte automático: registra en "Asociaciones manuales" las filas de ramas que no se compilan en producción, con Acción = quitar.
L2. Nombres de SP que no se pueden evaluar: armados en tiempo de ejecución, o tomados de una configuración que no está en el repo, de IOptions o de un parámetro.
    - Aplica si la sección 7.2 tiene "SP dinámico" o "Comando no resuelto", o si la 7.1 tiene filas "Solo en comentario".
    - Para cada caso, determina con el código y la configuración del repo si el SP realmente se ejecuta y cuál es.
      - Si lo logras, regístralo en "Asociaciones manuales": Acción = agregar, con el Archivo:línea de la llamada real. Si el reporte tenía una fila "Solo en comentario" para ese SP y endpoint, regístrala también, con Acción = quitar y el Archivo:línea del reporte.
      - Si el valor depende de una configuración que no está en el repo, anótalo en VERIFICACION.md como "SP sin determinar" y pregúntame. Mientras no lo confirme, usa Acción = "agregar (a confirmar)" / "quitar (a confirmar)" y no lo sumes en los totales ajustados (va en su propia columna).
L3. Llamadas que la herramienta no conecta.
    - Las llamadas por reflexión, dynamic o delegados guardados en diccionarios NO generan advertencia. Búscalas con "GetMethod\(|\.Invoke\(|\bdynamic\b|Activator\.CreateInstance|Dictionary<[^>]*\b(Func|Action)<".
    - Además, revisa si la 7.2 tiene:
      - "Llamada no resuelta": una llamada por nombre con receptor de tipo desconocido y más de 3 clases candidatas;
      - "Endpoint": el handler de una Minimal API no se resolvió.
    - Aplica si alguna de esas llamadas o handlers llega a acceso a datos. Sigue a mano el destino real y agrega sus SP a "Asociaciones manuales" (Acción = agregar). Si un SP de la 7.3 viene de esos métodos, también es un falso negativo.
L4. "Asociación ambigua" en la 7.2.
    - Significa que una query recibió varios SP migrados desde el comentario de un método llamador, y el reporte los marca todos con † como migrados a esa query.
    - Si aplica, revisa el código y determina qué SP reemplaza cada query. Registra las filas que sobran con Acción = quitar y agrega solo lo que falte.
L5. "Configuración ambigua" en la 7.2: una clase IOptions cuya sección no se pudo deducir. Si aplica, determina la sección real y agrega los SP que falten a "Asociaciones manuales" (Acción = agregar).
L6. Sobrecargas con la misma cantidad de parámetros. La herramienta sigue todas, así que puede sumar un SP de más. Aplica si en el muestreo encuentras una fila así: regístrala con Acción = quitar.
L7. Prefijos no configurados y SP sin package.
    - Si el PASO 2 encontró otros prefijos, vuelve a ejecutar con -PackagePrefixes / -ObjectPrefixes. Recuerda que la lista reemplaza a los valores por defecto.
    - Este punto se revisa SIEMPRE: un nombre sin ningún prefijo solo se detecta si se ejecuta con CommandType.StoredProcedure. Busca 'CommandType\.StoredProcedure|commandType:' con 4 líneas de contexto previo (Select-String -Context 4,0, sin bin/obj).
      - Si el nombre es un literal o una constante, confirma que está en el reporte.
      - Si llega como parámetro (spName, nombreSp...), busca las llamadas a ese wrapper y revisa el valor que se le pasa (ver L2).
L8. "Profundidad máxima" en la 7.2: vuelve a ejecutar con -MaxDepth 80.
L9. SP pendientes que, dentro de la BD, llaman a otros SP. En la tabla, márcalo "fuera de alcance": la herramienta solo ve el repo. Menciónalo en las recomendaciones solo si hay SP llamados directamente o hijos pendientes.

PASO 5. Decidir si hace falta corregir la herramienta
- Si la auditoría no encontró fallos de la herramienta que afecten a este repo, NO la modifiques: pasa al PASO 7.
- Lo que la herramienta no resuelve, pero puede completarse a mano (P1 si no se corrige, L1 a L6 y cada "Solo en comentario" que resulte ser una llamada real), va en "Asociaciones manuales". No se corrige en el script.
- Corrige solo los fallos de la herramienta que cambian el resultado sobre este repo y que tienen una corrección pequeña y localizada. Para cualquier otro cambio, pregúntame primero (regla 6).
- Todo fallo de la herramienta que NO se corrija aquí (por tamaño o porque no lo autorizo) se describe en cambios-herramienta/CAMBIOS.md, en "Pendientes para desarrollo", con el formato del PASO 6, punto 6. No lo dejes solo en VERIFICACION.md: ese archivo tiene datos de la empresa y no sale de este PC.

PASO 6. Protocolo de corrección (solo si el PASO 5 lo justifica)
1. Antes del primer cambio, copia el script original a cambios-herramienta/original/Analizar-SpEndpoints.ps1. La línea base ya está en reportes/base/.
2. Haz el cambio mínimo en Analizar-SpEndpoints.ps1 y respeta las reglas de la sección 10 de la guía:
   - UTF-8 con BOM;
   - el motor es C# 5: sin $"", ?., nameof, miembros con =>, tuplas, out var ni pattern matching;
   - ninguna línea del motor puede empezar con '@;
   - en las partes de PowerShell, no uses → — “ ” Ó Ñ.
3. Valida la sintaxis y el BOM con este comando. Debe imprimir "errores: 0 - BOM: EF BB BF":
     $f = (Resolve-Path ./Analizar-SpEndpoints.ps1).Path; $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$e); $b = Get-Content $f -Encoding Byte -TotalCount 3; 'errores: {0} - BOM: {1}' -f $e.Count, (($b | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')
   Ese comando no revisa el C# del motor: los errores de compilación aparecen al ejecutar el punto 4.
4. Vuelve a ejecutar con los mismos parámetros que la línea base, pero con -OutputPath ./reportes/corregido/sp-por-endpoint.md.
5. Compara el CSV corregido con el de la línea base:
     $cols = 'Verbo','Ruta','Handler','SP','SpHijo','Tipo','EstadoHijo','Nivel','Inferido','Archivo','Linea'
     $base = @(Import-Csv ./reportes/base/sp-por-endpoint.csv -Delimiter ';')
     $corr = @(Import-Csv ./reportes/corregido/sp-por-endpoint.csv -Delimiter ';')
     $dif  = @(Compare-Object $base $corr -Property $cols -CaseSensitive)
     'base: {0} filas | corregido: {1} filas | quitadas (<=): {2} | agregadas (=>): {3}' -f $base.Count, $corr.Count, @($dif | Where-Object SideIndicator -eq '<=').Count, @($dif | Where-Object SideIndicator -eq '=>').Count
     $dif | Sort-Object Ruta, SP, SpHijo, SideIndicator | Select-Object (@('SideIndicator') + $cols) | Export-Csv ./reportes/comparacion-base-vs-corregido.csv -Delimiter ';' -NoTypeInformation -Encoding UTF8
   - Cada fila agregada o quitada tiene que explicarse por la corrección.
   - Si aparece un cambio que no se explica, revisa la corrección o descártala.
6. Por cada corrección, y por cada pendiente para desarrollo, agrega a cambios-herramienta/CAMBIOS.md:
   - la versión base (1.1.0);
   - qué falla, explicado de forma genérica;
   - un ejemplo mínimo de C# que lo reproduzca, con nombres neutros inventados (por ejemplo RepoEjemplo, ServicioEjemplo y PCK_EJEMPLO.SP_EJEMPLO). Ningún identificador inventado (clase, método, campo, SP, ruta) puede ser igual a uno del repo ni contener una palabra del negocio (por ejemplo, el nombre de una entidad o de un módulo). Los prefijos PCK_/PKG_/SP_/FN_ y las palabras genéricas Repo, Servicio, Controller y Ejemplo están permitidos;
   - qué se cambió o qué habría que cambiar: la parte del script y la función o el método de la herramienta;
   - el efecto: cuántas filas se agregan o se quitan, sin nombres de la empresa.
7. Copia el script corregido a cambios-herramienta/Analizar-SpEndpoints.ps1.

PASO 7. Ejecución final y entregables
1. Ejecuta la versión final (la original o la corregida) sin -IncludeTrace, para que el reporte sea legible. La traza queda en la columna Traza del CSV:
     powershell -NoProfile -ExecutionPolicy Bypass -File ./Analizar-SpEndpoints.ps1 -RepoPath "<RUTA-DEL-REPO-API>" -OutputPath ./reportes/sp-por-endpoint.md -ExportCsv [mismos parámetros que la línea base]
   Si hubo más de una API y elegí un reporte por API, deja uno por API: reportes/<api>/sp-por-endpoint.md.
2. Escribe <herramienta>/reportes/VERIFICACION.md. Puede contener datos de la empresa: se queda en este PC. Debe tener:
   - un veredicto: "Sirve tal cual", "Sirve con asociaciones manuales", "Sirve con correcciones aplicadas" o "No sirve todavía", con la justificación;
   - las métricas: endpoints reales vs. detectados, filas muestreadas, filas correctas, % de precisión estimada y falsos negativos encontrados;
   - la tabla del pendiente y las limitaciones: Punto | ¿Aplica? | Evidencia | Acción;
   - la tabla "Asociaciones manuales": Acción (agregar / quitar / agregar (a confirmar) / quitar (a confirmar)) | Endpoint (VERBO /ruta, igual que en el reporte) | Package.SP | SP hijo | Tipo | Archivo:línea del SP (igual que en el reporte; para una llamada que la herramienta no vio, la línea de la llamada) | Motivo. En Motivo, indica el punto (P1, L1...) y el archivo:línea del eslabón que la herramienta no siguió (por ejemplo, la lambda de P1 o la rama #if de L1). Estas filas corrigen el reporte automático;
   - la tabla "Totales": endpoints con SP, SP listos, migrados con hijos, llamados directamente y pendientes, en tres columnas: reporte automático, ajustado (aplica las asociaciones manuales confirmadas) y ajustado incluyendo las "a confirmar";
   - los problemas encontrados, con su clasificación y lo que se hizo con cada uno;
   - si se corrigió la herramienta, el resumen de la comparación base vs. corregido;
   - recomendaciones de convención de comentarios para el equipo, para que el reporte sea exacto.
3. cambios-herramienta/:
   - Si quedaron pendientes para desarrollo, crea cambios-herramienta/CAMBIOS.md.
   - Solo si modificaste el script, agrega también cambios-herramienta/Analizar-SpEndpoints.ps1 y original/. No copies el script si no cambió.
   - Si creaste la carpeta, verifica que NO contiene datos de la empresa:
     - ejecuta este bloque. Junta los nombres de SP de los reportes, los tipos y namespaces del repo y los segmentos de las rutas, y los busca como palabra completa en cambios-herramienta/ (salvo en los .ps1, que se revisan en el punto siguiente). Agrega a mano a $t los nombres de métodos y campos del repo que aparezcan en los casos que describiste (por ejemplo, el campo de P1). Debe terminar con "coincidencias: 0":
         $repo = '<RUTA-DEL-REPO-API>'
         $generico = 'Api','Data','Dtos','Models','Services','Controllers','Repositories','Interfaces','Handlers','Queries','Commands','Entities','Domain','Application','Infrastructure','Common','Core','Web','Program','Startup','Tests'
         $t = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
         Get-ChildItem ./reportes -Recurse -File -Include *.md,*.csv | Select-String -AllMatches -Pattern '\b(PCK|PKG|PK)_\w+(\.\w+)?|\b(SP|FN|PRC)_\w+' | ForEach-Object { $_.Matches } | ForEach-Object { [void]$t.Add($_.Value) }
         Get-ChildItem $repo -Recurse -File -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Select-String -AllMatches -CaseSensitive -Pattern '\b(?:class|interface|record|struct|enum)\s+(\w+)', '^\s*namespace\s+([\w.]+)' | ForEach-Object { $_.Matches } | ForEach-Object { foreach ($p in $_.Groups[1].Value.Split('.')) { if ($p.Length -ge 4 -and $generico -notcontains $p) { [void]$t.Add($p) } } }
         Get-ChildItem ./reportes -Recurse -File -Filter *.csv | ForEach-Object { Import-Csv $_.FullName -Delimiter ';' } | ForEach-Object { $_.Ruta.Split('/') } | Where-Object { $_.Length -ge 4 -and $_ -notmatch '[{}]' -and $generico -notcontains $_ } | ForEach-Object { [void]$t.Add($_) }
         $hits = @(foreach ($x in $t) { Get-ChildItem ./cambios-herramienta -Recurse -File -Exclude *.ps1 | Select-String -Pattern ('\b' + [regex]::Escape($x) + '\b') | ForEach-Object { '{0}:{1}: [{2}] {3}' -f $_.Path, $_.LineNumber, $x, $_.Line.Trim() } })
         'Terminos revisados: {0} - coincidencias: {1}' -f $t.Count, $hits.Count; $hits
     - si hay un script corregido, compáralo con original/: la diferencia solo debe tener código genérico;
     - ningún archivo de reportes/ debe ir ahí.
4. Escríbeme en el chat un resumen corto con:
   - el veredicto;
   - las rutas de los reportes;
   - los totales: automático, ajustado y ajustado incluyendo las "a confirmar" (endpoints con SP, SP listos, migrados con hijos, llamados directamente, pendientes);
   - las asociaciones manuales;
   - si aplica, la lista de archivos de cambios-herramienta/.
````

---

## Después de la verificación

- Los reportes quedan en `reportes/`: `sp-por-endpoint.md`, `sp-por-endpoint.csv` y `VERIFICACION.md`. Las asociaciones manuales de `VERIFICACION.md` completan lo que el reporte automático no pudo detectar. **No los subas a ningún lado**: contienen información de la empresa.
- Si se creó `cambios-herramienta/`, revisa sus archivos. Si las políticas de tu empresa permiten sacarlos (no deben contener datos de la empresa), pásaselos a Claude en tu PC personal para que integre la corrección o el pendiente en el desarrollo y publique una versión nueva.
- **No subas `cambios-herramienta/` directamente a GitHub.** El script publicado se genera a partir del desarrollo, así que una edición directa se perdería en la próxima versión.
- Mientras tanto, puedes seguir usando en la empresa el script corregido.
