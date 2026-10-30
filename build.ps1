[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Installer,
    [switch]$Package,
    [string]$OutputDirectory = 'bin',
    [string]$InnoSetupCompiler,
    [string]$MakeAppx,
    [string]$PackageIdentityName,
    [string]$PackagePublisher,
    [string]$PackagePublisherDisplayName
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
$output = (Resolve-Path -LiteralPath $output).ProviderPath
$packageStage = Join-Path $output 'msix'
$iconParameters = @{ OutputDirectory = $output }
if ($Package) {
    if (Test-Path -LiteralPath $packageStage) { Remove-Item -LiteralPath $packageStage -Recurse -Force }
    $iconParameters.PackageAssetDirectory = Join-Path $packageStage 'Assets'
}
& (Join-Path $PSScriptRoot 'tools\New-AppIcon.ps1') @iconParameters
$appIcon = Join-Path $output 'MD301Plus.ico'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' |
    Sort-Object Name | ForEach-Object { $_.FullName })
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll',
    'System.Windows.Forms.dll', 'System.Runtime.Serialization.dll', 'Accessibility.dll')
$common = @('/nologo', '/warn:4', '/warnaserror+', '/optimize+', '/checked+', '/platform:anycpu')
$common += '/win32icon:' + $appIcon
$common += '/resource:' + $appIcon + ',MD301Plus.AppIcon'
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

if ($Installer) {
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($app)
    if ($version.ProductVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw ('Set AssemblyInformationalVersion in src\AssemblyInfo.cs to major.minor.patch (found "{0}").' -f
            $version.ProductVersion)
    }
    $iscc = $InnoSetupCompiler
    if (-not $iscc) {
        $command = Get-Command 'ISCC.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) { $iscc = $command.Path }
    }
    if (-not $iscc) {
        $iscc = @(${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs')) |
            Where-Object { $_ } |
            ForEach-Object { Get-ChildItem -Path (Join-Path $_ 'Inno Setup *\ISCC.exe') -ErrorAction SilentlyContinue } |
            Sort-Object -Descending -Property {
                $info = $_.VersionInfo
                New-Object Version $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart
            } |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $iscc -or -not (Test-Path -LiteralPath $iscc)) {
        throw ('Building the installer needs Inno Setup 6.5.2 or later from https://jrsoftware.org/isdl.php. ' +
            'Install it, or pass -InnoSetupCompiler with the path to ISCC.exe.')
    }
    & $iscc '/Qp' ('/DAppVersion=' + $version.ProductVersion) ('/DFileVersion=' + $version.FileVersion) `
        ('/DAppBinDir=' + $output) ('/O' + $output) (Join-Path $PSScriptRoot 'installer\MD301Plus.iss')
    if ($LASTEXITCODE -ne 0) {
        throw 'Installer compilation failed.'
    }
    Write-Output ('Built ' + (Join-Path $output 'MD301Plus-Setup.exe'))
}

if ($Package) {
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($app).FileVersion
    if ($fileVersion -notmatch '^\d+\.\d+\.\d+\.0$') {
        throw ('The Microsoft Store needs a package version that ends in .0. Set AssemblyFileVersion in ' +
            'src\AssemblyInfo.cs to major.minor.patch.0 (found "{0}").' -f $fileVersion)
    }
    # Local builds use placeholders; Store uploads must use the identity from Partner Center.
    $identity = @{
        IdentityName = $(if ($PackageIdentityName) { $PackageIdentityName } else { 'HugoMSFT.MD301Plus' })
        Publisher = $(if ($PackagePublisher) { $PackagePublisher } else { 'CN=HugoMSFT' })
        PublisherDisplayName = $(if ($PackagePublisherDisplayName) { $PackagePublisherDisplayName } else { 'HugoMSFT' })
        Version = $fileVersion
    }
    $manifest = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'packaging\AppxManifest.xml'))
    foreach ($token in $identity.GetEnumerator()) {
        $manifest = $manifest.Replace('{{' + $token.Key + '}}', [System.Security.SecurityElement]::Escape($token.Value))
    }
    if ($manifest -match '\{\{\w+\}\}') {
        throw ('packaging\AppxManifest.xml has an unknown placeholder: ' + $Matches[0])
    }
    New-Item -ItemType Directory -Path $packageStage -Force | Out-Null
    Copy-Item -LiteralPath $app, ($app + '.config') -Destination $packageStage
    [System.IO.File]::WriteAllText((Join-Path $packageStage 'AppxManifest.xml'), $manifest,
        (New-Object System.Text.UTF8Encoding $false))

    $makeAppxPath = $MakeAppx
    if (-not $makeAppxPath) {
        $command = Get-Command 'makeappx.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) { $makeAppxPath = $command.Path }
    }
    if (-not $makeAppxPath -and ${env:ProgramFiles(x86)}) {
        $makeAppxPath = Get-ChildItem -Path (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\*\x64\makeappx.exe') `
            -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Parent.Name -match '^\d+(\.\d+){3}$' } |
            Sort-Object -Descending -Property { [Version]$_.Directory.Parent.Name } |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $makeAppxPath -or -not (Test-Path -LiteralPath $makeAppxPath)) {
        throw ('Building the Store package needs MakeAppx.exe from the Windows SDK ' +
            '(https://developer.microsoft.com/windows/downloads/windows-sdk/). ' +
            'Install it, or pass -MakeAppx with the path to makeappx.exe.')
    }
    $msix = Join-Path $output 'MD301Plus.msix'
    $packOutput = & $makeAppxPath pack /o /d $packageStage /p $msix 2>&1
    if ($LASTEXITCODE -ne 0) {
        $packOutput | Write-Output
        throw 'Store package creation failed.'
    }
    Write-Output "Built $msix"
}
