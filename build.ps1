[CmdletBinding()]
param(
    [switch]$Test,
    [string]$OutputDirectory = 'bin'
)

$ErrorActionPreference = 'Stop'
$runtime = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$compiler = Join-Path $runtime 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework C# compiler is missing. Install .NET Framework 4.8 (4.8.1 on ARM64).'
}

$output = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $PSScriptRoot $OutputDirectory
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' |
    Sort-Object Name | ForEach-Object { $_.FullName })
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll',
    'System.Windows.Forms.dll', 'System.Runtime.Serialization.dll', 'Accessibility.dll')
$common = @('/nologo', '/warn:4', '/warnaserror+', '/optimize+', '/checked+', '/platform:anycpu')
foreach ($reference in $references) {
    $common += '/reference:' + (Join-Path $runtime $reference)
}

$app = Join-Path $output 'MD301Plus.exe'
& $compiler @common '/target:winexe' (('/out:{0}' -f $app)) `
    (('/win32manifest:{0}' -f (Join-Path $PSScriptRoot 'src\app.manifest'))) @sources
if ($LASTEXITCODE -ne 0) {
    throw 'Application compilation failed.'
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\MD301Plus.exe.config') -Destination ($app + '.config')
Write-Output "Built $app"

if ($Test) {
    $testExe = Join-Path $output 'MD301Plus.Tests.exe'
    & $compiler @common '/debug:pdbonly' '/target:exe' '/main:MD301Plus.Tests.TestRunner' `
        (('/out:{0}' -f $testExe)) `
        (('/win32manifest:{0}' -f (Join-Path $PSScriptRoot 'src\app.manifest'))) `
        @sources (Join-Path $PSScriptRoot 'tests\Tests.cs') (Join-Path $PSScriptRoot 'tests\MediaProbe.cs')
    if ($LASTEXITCODE -ne 0) {
        throw 'Test compilation failed.'
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\MD301Plus.exe.config') -Destination ($testExe + '.config')
    & $testExe
    if ($LASTEXITCODE -ne 0) {
        throw 'Tests failed.'
    }
}
