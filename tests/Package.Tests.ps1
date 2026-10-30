[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Package,
    [string]$SignTool,
    [switch]$Force
)

# End-to-end test for the Microsoft Store package (MD301Plus.msix): sign a copy with a temporary test
# certificate, install it, open it from Start, check the second-launch handoff and remove it. The Store
# signs the real upload, so the test certificate never ships. Trusting the certificate needs administrator
# rights and changes this computer while the test runs, so the test refuses to run outside CI unless -Force
# is passed. CI runs it on a clean runner after build.ps1 -Package.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$appId = 'MD301Plus'
$foundationNamespace = 'http://schemas.microsoft.com/appx/manifest/foundation/windows10'
$uap10Namespace = 'http://schemas.microsoft.com/appx/manifest/uap/windows10/10'
$desktopNamespace = 'http://schemas.microsoft.com/appx/manifest/desktop/windows10'
$settingsDir = Join-Path $env:LOCALAPPDATA 'MD301Plus'
$workDir = Join-Path ([IO.Path]::GetTempPath()) ('MD301Plus-PackageTest-' + [Guid]::NewGuid().ToString('N'))
$started = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'

function Assert($Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    Write-Host "ok - $Message"
}

function Wait-For([scriptblock]$Condition, [int]$TimeoutSeconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return [bool](& $Condition)
}

function Test-AppRunning {
    $mutex = $null
    if ([System.Threading.Mutex]::TryOpenExisting('Local\MD301Plus', [ref]$mutex)) {
        $mutex.Dispose()
        return $true
    }
    return $false
}

function Test-SettingsOpen([System.Diagnostics.Process]$Process) {
    $Process.Refresh()
    return (-not $Process.HasExited) -and $Process.MainWindowHandle -ne [IntPtr]::Zero
}

function Find-SdkTool([string]$Name) {
    $command = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Path }
    if (-not ${env:ProgramFiles(x86)}) { return $null }
    return Get-ChildItem -Path (Join-Path ${env:ProgramFiles(x86)} ('Windows Kits\10\bin\*\x64\' + $Name)) `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Parent.Name -match '^\d+(\.\d+){3}$' } |
        Sort-Object -Descending -Property { [Version]$_.Directory.Parent.Name } |
        Select-Object -First 1 -ExpandProperty FullName
}

# Starts the app the way Start does, and returns the new process ID.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace MD301PlusPackageTest
{
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IApplicationActivationManager
    {
        void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, int options, out uint processId);
    }

    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    internal class ApplicationActivationManager
    {
    }

    public static class PackageLauncher
    {
        public static int Launch(string appUserModelId)
        {
            IApplicationActivationManager manager = (IApplicationActivationManager)new ApplicationActivationManager();
            uint processId;
            manager.ActivateApplication(appUserModelId, null, 0, out processId);
            return (int)processId;
        }
    }
}
'@

function Start-PackagedApp([string]$AppUserModelId) {
    $process = Get-Process -Id ([MD301PlusPackageTest.PackageLauncher]::Launch($AppUserModelId)) -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null } # It already finished.
    try { $null = $process.Handle } catch { return $null } # The handle keeps ExitCode available after it exits.
    $started.Add($process)
    return $process
}

if (-not $env:CI -and -not $Force) {
    throw ('This test trusts a temporary certificate on this computer and installs MD301 Plus as a Store package. ' +
        'Run it in CI, or pass -Force on a disposable test computer.')
}
$principal = New-Object Security.Principal.WindowsPrincipal ([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run the package test as administrator; it trusts a temporary test certificate on this computer.'
}
$packagePath = (Resolve-Path -LiteralPath $Package).ProviderPath
if (-not $SignTool) { $SignTool = Find-SdkTool 'signtool.exe' }
if (-not $SignTool -or -not (Test-Path -LiteralPath $SignTool)) {
    throw 'The package test needs SignTool.exe from the Windows SDK. Install it, or pass -SignTool with its path.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $reader = New-Object System.IO.StreamReader ($zip.GetEntry('AppxManifest.xml').Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally {
    $zip.Dispose()
}
$identity = $manifest.Package.Identity
if (Test-AppRunning) {
    throw 'Exit MD301 Plus before running the package test.'
}
if (@(Get-AppxPackage -Name $identity.Name).Count -gt 0) {
    throw (('The {0} package is already installed for this user. This test installs and removes it, so run it ' +
        'on a clean account such as CI.') -f $identity.Name)
}

$settingsExisted = Test-Path -LiteralPath $settingsDir
$certificate = $null
$trusted = $null
New-Item -ItemType Directory -Path $workDir -Force | Out-Null
try {
    $namespaces = New-Object System.Xml.XmlNamespaceManager $manifest.NameTable
    $namespaces.AddNamespace('m', $foundationNamespace)
    $namespaces.AddNamespace('desktop', $desktopNamespace)
    $application = $manifest.SelectSingleNode("/m:Package/m:Applications/m:Application[@Id='$appId']", $namespaces)
    Assert ($null -ne $application) "the package declares the $appId app"
    Assert ($application.GetAttribute('Parameters', $uap10Namespace) -eq '--settings') 'opening the app from Start asks for Settings'
    $startup = $application.SelectSingleNode("m:Extensions/desktop:Extension[@Category='windows.startupTask']", $namespaces)
    Assert ($null -ne $startup -and $startup.GetAttribute('Parameters', $uap10Namespace) -eq '--startup') 'the sign-in task starts the app quietly'

    $signed = Join-Path $workDir 'MD301Plus.msix'
    Copy-Item -LiteralPath $packagePath -Destination $signed
    $certificate = New-SelfSignedCertificate -Type Custom -Subject $identity.Publisher -KeyUsage DigitalSignature `
        -FriendlyName 'MD301 Plus package test' -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    $publicCertificate = Join-Path $workDir 'MD301Plus-test.cer'
    Export-Certificate -Cert $certificate -FilePath $publicCertificate | Out-Null
    $trusted = Import-Certificate -FilePath $publicCertificate -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
    $signOutput = & $SignTool sign /fd SHA256 /sha1 $certificate.Thumbprint $signed 2>&1
    if ($LASTEXITCODE -ne 0) {
        $signOutput | Write-Host
        throw 'Signing the test copy of the package failed.'
    }

    Add-AppxPackage -Path $signed
    $installed = Get-AppxPackage -Name $identity.Name
    Assert ($null -ne $installed) 'installs the Store package'
    Assert ($installed.Version -eq $identity.Version) "installs version $($identity.Version)"
    $appUserModelId = $installed.PackageFamilyName + '!' + $appId

    $app = Start-PackagedApp $appUserModelId
    Assert ($null -ne $app) 'opening MD301 Plus from Start starts the packaged app'
    $null = Wait-For { $app.HasExited -or (Test-SettingsOpen $app) }
    Assert (Test-SettingsOpen $app) 'the packaged app opens Settings'
    Assert ($app.Path -like ($installed.InstallLocation.TrimEnd('\') + '\*')) 'runs MD301Plus.exe from the installed package'
    $null = $app.CloseMainWindow()
    $null = Wait-For { $app.Refresh(); $app.HasExited -or $app.MainWindowHandle -eq [IntPtr]::Zero }
    Assert (-not $app.HasExited -and $app.MainWindowHandle -eq [IntPtr]::Zero) 'closing Settings keeps the packaged app running in the notification area'

    $second = Start-PackagedApp $appUserModelId
    if ($null -ne $second) {
        Assert ($second.WaitForExit(15000)) 'opening MD301 Plus again hands off to the running app'
        Assert ($second.ExitCode -eq 0) "the second launch exits cleanly (exit code $($second.ExitCode))"
    }
    $null = Wait-For { $app.HasExited -or (Test-SettingsOpen $app) } 15
    Assert (Test-SettingsOpen $app) 'opening MD301 Plus again opens Settings in the running app'

    $packageSettings = Join-Path $env:LOCALAPPDATA ('Packages\{0}\LocalCache\Local\MD301Plus\settings.json' -f $installed.PackageFamilyName)
    Assert ((Test-Path -LiteralPath $packageSettings) -or (Test-Path -LiteralPath (Join-Path $settingsDir 'settings.json'))) 'the packaged app saves its settings'

    Stop-Process -Id $app.Id -Force
    Assert ($app.WaitForExit(15000)) 'the packaged app stops'
    Remove-AppxPackage -Package $installed.PackageFullName
    Assert (@(Get-AppxPackage -Name $identity.Name).Count -eq 0) 'removing the package uninstalls MD301 Plus'

    Write-Host 'PASS: Store package install, Start launch, second launch and removal'
}
finally {
    foreach ($process in $started) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }
    foreach ($leftover in @(Get-AppxPackage -Name $identity.Name)) {
        try { Remove-AppxPackage -Package $leftover.PackageFullName }
        catch { Write-Host "Cleanup could not remove $($leftover.PackageFullName): $($_.Exception.Message)" }
    }
    if ($trusted) { Remove-Item -LiteralPath ('Cert:\LocalMachine\TrustedPeople\' + $trusted.Thumbprint) -ErrorAction SilentlyContinue }
    if ($certificate) { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $certificate.Thumbprint) -ErrorAction SilentlyContinue }
    if (-not $settingsExisted -and (Test-Path -LiteralPath $settingsDir)) {
        Remove-Item -LiteralPath $settingsDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
