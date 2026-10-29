[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Executable
)

$ErrorActionPreference = 'Stop'
$executablePath = [System.IO.Path]::GetFullPath($Executable)
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Application executable not found: $executablePath"
}

$startMenu = [Environment]::GetFolderPath([Environment+SpecialFolder]::StartMenu)
$shortcutPath = Join-Path $startMenu 'Programs\MD301 Plus.lnk'
New-Item -ItemType Directory -Path (Split-Path -Parent $shortcutPath) -Force | Out-Null

$shell = New-Object -ComObject WScript.Shell
try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executablePath
    $shortcut.Arguments = '--settings'
    $shortcut.WorkingDirectory = Split-Path -Parent $executablePath
    $shortcut.IconLocation = $executablePath + ',0'
    $shortcut.Description = 'Open MD301 Plus settings and action ring'
    $shortcut.Save()
} finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}

Write-Output "Installed Start Menu shortcut: $shortcutPath"
