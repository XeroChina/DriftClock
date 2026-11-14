# Builds dist/DriftClock.scr with the .NET Framework compiler.
# Use this when no dotnet SDK is installed: `dotnet build` needs the SDK, csc only needs the .NET Framework.
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src/DriftClock'
$out = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $out | Out-Null

$csc = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw 'csc.exe not found (need .NET Framework 4.x installed)' }

$sources = Get-ChildItem $src -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /optimize+ "/out:$out\DriftClock.scr" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.Web.Extensions.dll /r:System.Security.dll `
    "/win32manifest:$src\app.manifest" $sources
if ($LASTEXITCODE -ne 0) { throw "compile failed ($LASTEXITCODE)" }

$scr = Join-Path $out 'DriftClock.scr'
"built $scr ({0:N0} bytes)" -f (Get-Item $scr).Length
