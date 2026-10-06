$ErrorActionPreference = 'Stop'
$parts = Get-ChildItem (Join-Path $PSScriptRoot 'cs') -Filter *.cs | Sort-Object Name
$src = ($parts | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`r`n"
$src = $src.Replace('SPA_NS', 'SpaTest' + [Guid]::NewGuid().ToString('N').Substring(0,8))
try {
  Add-Type -TypeDefinition $src -Language CSharp -ReferencedAssemblies 'System.Core','System.Xml','System.Xml.Linq' -ErrorAction Stop
  'COMPILE OK'
} catch {
  $_.Exception.Message
  if ($_.Exception.Errors) { $_.Exception.Errors | Select-Object -First 40 | ForEach-Object { "{0}: {1}" -f $_.Line, $_.ErrorText } }
}
