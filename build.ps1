# Builds ClaudeUsageTracker.exe with the C# compiler that ships with Windows
# (.NET Framework 4.x) — no SDK or Visual Studio required.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }

$out = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $out | Out-Null

$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$cscArgs = @(
    '/nologo', '/target:winexe', '/optimize+', '/platform:anycpu',
    "/out:$(Join-Path $out 'ClaudeUsageTracker.exe')",
    "/win32manifest:$(Join-Path $root 'app.manifest')",
    "/win32icon:$(Join-Path $root 'assets\app.ico')",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Security.dll', '/r:System.Web.Extensions.dll'
) + $sources

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Write-Host "Built $(Join-Path $out 'ClaudeUsageTracker.exe')"
