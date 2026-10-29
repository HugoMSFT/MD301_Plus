[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Setup
)

# End-to-end test for MD301Plus-Setup.exe: install, Start menu, sign-in option, second launch,
# running-app guard, upgrade and uninstall. It changes the current user's profile, so it refuses to run
# while MD301 Plus is installed or running. CI runs it on a clean runner after build.ps1 -Installer.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$appName = 'MD301 Plus'
$setupPath = (Resolve-Path -LiteralPath $Setup).ProviderPath
$builtExe = Join-Path (Split-Path -Parent $setupPath) 'MD301Plus.exe'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\MD301 Plus'
$installedExe = Join-Path $installDir 'MD301Plus.exe'
$uninstaller = Join-Path $installDir 'unins000.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'MD301 Plus.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
$settingsDir = Join-Path $env:LOCALAPPDATA 'MD301Plus'
$logDir = Join-Path ([IO.Path]::GetTempPath()) ('MD301Plus-InstallerTest-' + [Guid]::NewGuid().ToString('N'))
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

function Get-RunValue {
    $key = Get-Item -LiteralPath $runKey -ErrorAction SilentlyContinue
    if ($null -eq $key) { return $null }
    try { return $key.GetValue($appName) } finally { $key.Close() }
}

function Get-UninstallEntry {
    Get-ChildItem -LiteralPath $uninstallKey -ErrorAction SilentlyContinue |
        Where-Object { $_.GetValue('DisplayName') -eq $appName }
}

function Invoke-Installer([string]$FilePath, [string[]]$Arguments, [string]$LogName) {
    $log = Join-Path $logDir $LogName
    $process = Start-Process -FilePath $FilePath -ArgumentList ($Arguments + ('/LOG="{0}"' -f $log)) -Wait -PassThru
    return $process.ExitCode
}

function Start-App([string[]]$Arguments) {
    $parameters = @{ FilePath = $installedExe; PassThru = $true }
    if ($Arguments) { $parameters.ArgumentList = $Arguments }
    $process = Start-Process @parameters
    $null = $process.Handle # Keeps ExitCode available after the process exits.
    $started.Add($process)
    return $process
}

function Test-SettingsOpen([System.Diagnostics.Process]$Process) {
    $Process.Refresh()
    return (-not $Process.HasExited) -and $Process.MainWindowHandle -ne [IntPtr]::Zero
}

if (-not (Test-Path -LiteralPath $builtExe)) {
    throw "Build output $builtExe is missing. Run build.ps1 -Installer first."
}
if (Test-AppRunning) {
    throw 'Exit MD301 Plus before running the installer test.'
}
if ((Test-Path -LiteralPath $installDir) -or (Test-Path -LiteralPath $shortcut) -or
    ($null -ne (Get-RunValue)) -or @(Get-UninstallEntry).Count -gt 0) {
    throw ('MD301 Plus is already installed for this user (or its Start menu shortcut or sign-in entry exists). ' +
        'This test installs and uninstalls it, so run it on a clean account such as CI.')
}

$settingsExisted = Test-Path -LiteralPath $settingsDir
$passed = $false
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
try {
    $productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($builtExe).ProductVersion

    $code = Invoke-Installer $setupPath @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/TASKS=startup') 'install.log'
    Assert ($code -eq 0) "silent install succeeds (exit code $code)"
    Assert (Test-Path -LiteralPath $installedExe) "installs MD301Plus.exe in $installDir"
    Assert (Test-Path -LiteralPath ($installedExe + '.config')) 'installs MD301Plus.exe.config'
    Assert ((Get-FileHash -LiteralPath $installedExe).Hash -eq (Get-FileHash -LiteralPath $builtExe).Hash) 'installs the app from this build'
    Assert (Test-Path -LiteralPath $shortcut) 'adds MD301 Plus to the Start menu'
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    Assert ($link.TargetPath -eq $installedExe -and $link.Arguments -eq '--settings') 'the Start menu shortcut opens Settings in the installed app'
    $entries = @(Get-UninstallEntry)
    Assert ($entries.Count -eq 1) 'adds one Installed apps entry'
    Assert ($entries[0].GetValue('DisplayVersion') -eq $productVersion) "the Installed apps entry shows version $productVersion"
    Assert ($entries[0].GetValue('Publisher') -eq 'HugoMSFT') 'the Installed apps entry shows the publisher'
    Assert (([string]$entries[0].GetValue('InstallLocation')).TrimEnd('\') -eq $installDir) 'installs per user without administrator rights'
    Assert ((Get-RunValue) -eq ('"{0}"' -f $installedExe)) 'the sign-in option starts the installed app'

    $app = Start-App @('--settings')
    $null = Wait-For { $app.HasExited -or (Test-SettingsOpen $app) }
    Assert (Test-SettingsOpen $app) 'the installed app starts and opens Settings'
    $null = $app.CloseMainWindow()
    $null = Wait-For { $app.Refresh(); $app.HasExited -or $app.MainWindowHandle -eq [IntPtr]::Zero }
    Assert (-not $app.HasExited -and $app.MainWindowHandle -eq [IntPtr]::Zero) 'closing Settings keeps the app running in the notification area'
    $second = Start-App @('--settings')
    Assert ($second.WaitForExit(15000)) 'starting MD301 Plus again hands off to the running app'
    Assert ($second.ExitCode -eq 0) "the second launch exits cleanly (exit code $($second.ExitCode))"
    $null = Wait-For { $app.HasExited -or (Test-SettingsOpen $app) } 15
    Assert (Test-SettingsOpen $app) 'starting MD301 Plus again opens Settings in the running app'

    $code = Invoke-Installer $setupPath @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-') 'while-running.log'
    Assert ($code -ne 0) "Setup will not replace the app while it is running (exit code $code)"
    Assert (-not $app.HasExited) 'Setup leaves the running app alone'
    Stop-Process -Id $app.Id -Force
    Assert ($app.WaitForExit(15000)) 'the running app stops'
    Assert (Wait-For { -not (Test-AppRunning) } 15) 'the single-instance lock is released'

    $code = Invoke-Installer $setupPath @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/MERGETASKS=!startup') 'upgrade.log'
    Assert ($code -eq 0) "installing over an existing install succeeds (exit code $code)"
    Assert (@(Get-UninstallEntry).Count -eq 1) 'installing again keeps one Installed apps entry'
    Assert ($null -eq (Get-RunValue)) 'clearing the sign-in option removes the sign-in entry'
    Assert (Test-Path -LiteralPath $shortcut) 'installing again keeps the Start menu shortcut'

    $code = Invoke-Installer $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') 'uninstall.log'
    Assert ($code -eq 0) "silent uninstall succeeds (exit code $code)"
    Assert (Wait-For { (-not (Test-Path -LiteralPath $installDir)) -and @(Get-UninstallEntry).Count -eq 0 } 60) 'uninstall removes the app folder and the Installed apps entry'
    Assert (-not (Test-Path -LiteralPath $shortcut)) 'uninstall removes the Start menu shortcut'
    Assert ($null -eq (Get-RunValue)) 'uninstall leaves no sign-in entry'
    Assert (Test-Path -LiteralPath (Join-Path $settingsDir 'settings.json')) 'uninstall keeps the user settings'

    $passed = $true
    Write-Host 'PASS: installer install, Start menu, sign-in option, second launch, running-app guard, upgrade and uninstall'
}
catch {
    Get-ChildItem -LiteralPath $logDir -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "----- $($_.Name) -----"
        Get-Content -LiteralPath $_.FullName | Write-Host
    }
    throw
}
finally {
    foreach ($process in $started) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }
    if (Test-Path -LiteralPath $uninstaller) {
        $null = Wait-For { -not (Test-AppRunning) } 15
        try { Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait }
        catch { Write-Host "Cleanup could not uninstall MD301 Plus: $($_.Exception.Message)" }
    }
    if (-not $settingsExisted -and (Test-Path -LiteralPath $settingsDir)) {
        Remove-Item -LiteralPath $settingsDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($passed) { Remove-Item -LiteralPath $logDir -Recurse -Force -ErrorAction SilentlyContinue }
}
