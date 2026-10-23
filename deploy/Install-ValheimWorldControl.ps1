$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework

$source = Split-Path -Parent $PSCommandPath
$application = Join-Path $source 'ValheimWorldControl.exe'
if (-not (Test-Path -LiteralPath $application)) {
    throw 'ValheimWorldControl.exe is missing from the installer folder.'
}

$installDirectory = Join-Path $env:LOCALAPPDATA 'Valheim World Control'
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -LiteralPath $application -Destination (Join-Path $installDirectory 'ValheimWorldControl.exe') -Force

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Valheim World Control.lnk'))
$shortcut.TargetPath = Join-Path $installDirectory 'ValheimWorldControl.exe'
$shortcut.WorkingDirectory = $installDirectory
$shortcut.Description = 'Valheim World Control dashboard'
$shortcut.Save()

Start-Process (Join-Path $installDirectory 'ValheimWorldControl.exe')
[System.Windows.MessageBox]::Show('Valheim World Control is installed. Enter the HTTPS address for your own control API.', 'Valheim World Control') | Out-Null
