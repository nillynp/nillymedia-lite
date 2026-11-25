# ============================================================================
# NillyMedia Lite — Universal Windows 10/11 Installer (Zero Admin · Portable)
# created by Nilly graphics (Sotiris Christos) — https://nillygraphics.com/
# Ultra-Light Native Media Player & 1-Click Voice Recorder (Replaces WMP + Rec)
# ============================================================================
param(
    [switch]$Silent,
    [switch]$OpenDefaultApps
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exePath = Join-Path $root 'NillyMediaLite.exe'
$icoPath = Join-Path $root 'NillyMediaLite.ico'
$srcPath = Join-Path $root 'desktop\NillyMediaLite.cs'

Write-Host ''
Write-Host '  ============================================================' -ForegroundColor Cyan
Write-Host '   NillyMedia Lite — Ultra-Light Media Player & Voice Recorder' -ForegroundColor White
Write-Host '   created by Nilly graphics (Sotiris Christos)' -ForegroundColor DarkGray
Write-Host '   https://nillygraphics.com/' -ForegroundColor DarkGray
Write-Host '  ============================================================' -ForegroundColor Cyan
Write-Host ''

# 1. Unblock downloaded files if extracted from GitHub Portable ZIP
Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

# 2. Auto-compile NillyMediaLite.exe on any Windows 10/11 PC if missing or older than source
$needBuild = (-not (Test-Path $exePath)) -or ((Test-Path $srcPath) -and ((Get-Item $srcPath).LastWriteTimeUtc -gt (Get-Item $exePath).LastWriteTimeUtc))
if ($needBuild) {
    $fxDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
    if (-not (Test-Path "$fxDir\csc.exe")) { $fxDir = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319' }
    $csc = Join-Path $fxDir 'csc.exe'
    $wpf = Join-Path $fxDir 'WPF'
    if (-not (Test-Path $csc)) { throw 'Δεν βρέθηκε ο .NET Framework 4.x C# compiler (csc.exe).' }
    Write-Host '  [1/3] Μεταγλώττιση NillyMediaLite.exe (100% Native WPF Host)...' -ForegroundColor Yellow
    $cscArgs = @(
        '/nologo', '/target:winexe', '/optimize+', '/utf8output', "/out:$exePath",
        '/r:System.dll', '/r:System.Core.dll',
        "/r:$wpf\PresentationFramework.dll", "/r:$wpf\PresentationCore.dll", "/r:$wpf\WindowsBase.dll",
        "/r:$fxDir\System.Xaml.dll"
    )
    if (Test-Path $icoPath) { $cscArgs += "/win32icon:$icoPath" }
    $cscArgs += $srcPath
    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exePath)) {
        throw "Αποτυχία μεταγλώττισης NillyMediaLite.exe (exit $LASTEXITCODE)"
    }
    Write-Host ("        OK -> " + [math]::Round((Get-Item $exePath).Length / 1KB, 1) + " KB") -ForegroundColor Green
} else {
    Write-Host '  [1/3] NillyMediaLite.exe έτοιμο.' -ForegroundColor Green
}

# 3. Register NillyMedia Lite for 14 safe Media formats in HKCU (Zero Admin!)
Write-Host '  [2/3] Εγγραφή του NillyMedia Lite στα Windows (14 Audio/Video formats)...' -ForegroundColor Yellow
$iconRef = if (Test-Path $icoPath) { "$icoPath,0" } else { "$exePath,0" }
$openCmd = "`"$exePath`" `"%1`""

$progKey = 'HKCU:\Software\Classes\NillyMediaLite.Media'
New-Item -Path "$progKey\DefaultIcon" -Force | Out-Null
New-Item -Path "$progKey\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path $progKey -Name '(Default)' -Value 'NillyMedia Lite Media File'
Set-ItemProperty -Path $progKey -Name 'FriendlyTypeName' -Value 'NillyMedia Lite Media File'
Set-ItemProperty -Path "$progKey\DefaultIcon" -Name '(Default)' -Value $iconRef
Set-ItemProperty -Path "$progKey\shell\open" -Name 'FriendlyAppName' -Value 'NillyMedia Lite'
Set-ItemProperty -Path "$progKey\shell\open\command" -Name '(Default)' -Value $openCmd

$appKey = 'HKCU:\Software\Classes\Applications\NillyMediaLite.exe'
New-Item -Path "$appKey\DefaultIcon" -Force | Out-Null
New-Item -Path "$appKey\shell\open\command" -Force | Out-Null
New-Item -Path "$appKey\SupportedTypes" -Force | Out-Null
Set-ItemProperty -Path $appKey -Name 'FriendlyAppName' -Value 'NillyMedia Lite'
Set-ItemProperty -Path "$appKey\DefaultIcon" -Name '(Default)' -Value $iconRef
Set-ItemProperty -Path "$appKey\shell\open\command" -Name '(Default)' -Value $openCmd

$capKey = 'HKCU:\Software\NillyGraphics\NillyMediaLite\Capabilities'
New-Item -Path "$capKey\FileAssociations" -Force | Out-Null
Set-ItemProperty -Path $capKey -Name 'ApplicationName' -Value 'NillyMedia Lite'
Set-ItemProperty -Path $capKey -Name 'ApplicationDescription' -Value 'Ultra-Light Native Media Player & 1-Click Voice Recorder — created by Nilly graphics (Sotiris Christos) — https://nillygraphics.com/'

$mediaExts = @('.mp4','.m4v','.mov','.wmv','.avi','.mkv','.webm','.mp3','.wav','.m4a','.aac','.wma','.flac','.ogg')
foreach ($ext in $mediaExts) {
    $owp = "HKCU:\Software\Classes\$ext\OpenWithProgids"
    New-Item -Path $owp -Force | Out-Null
    Set-ItemProperty -Path $owp -Name 'NillyMediaLite.Media' -Value ([byte[]]@()) -Type Binary
    Set-ItemProperty -Path "$appKey\SupportedTypes" -Name $ext -Value ''
    Set-ItemProperty -Path "$capKey\FileAssociations" -Name $ext -Value 'NillyMediaLite.Media'
}

New-Item -Path 'HKCU:\Software\RegisteredApplications' -Force | Out-Null
Set-ItemProperty -Path 'HKCU:\Software\RegisteredApplications' -Name 'NillyMediaLite' -Value 'Software\NillyGraphics\NillyMediaLite\Capabilities'

try {
    Add-Type -Namespace NillyMediaShell -Name Notify -MemberDefinition '[System.Runtime.InteropServices.DllImport("shell32.dll")] public static extern void SHChangeNotify(int wEventId, int uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);' -ErrorAction SilentlyContinue
    [NillyMediaShell.Notify]::SHChangeNotify(0x08000000, 0x2000, [IntPtr]::Zero, [IntPtr]::Zero)
} catch {}

Write-Host ("        OK -> Εγγράφηκαν " + $mediaExts.Count + " formats στο Άνοιγμα με... & στις Προεπιλεγμένες Εφαρμογές!") -ForegroundColor Green

# 4. Desktop & Start Menu Shortcuts
Write-Host '  [3/3] Δημιουργία συντομεύσεων (Επιφάνεια Εργασίας & Start Menu)...' -ForegroundColor Yellow
$wsh = New-Object -ComObject WScript.Shell
$desktopDir = [Environment]::GetFolderPath('Desktop')
$startMenuDir = Join-Path ([Environment]::GetFolderPath('Programs')) 'Nilly graphics'
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null

foreach ($lnkPath in @(
    (Join-Path $desktopDir 'NillyMedia Lite.lnk'),
    (Join-Path $startMenuDir 'NillyMedia Lite.lnk')
)) {
    $sc = $wsh.CreateShortcut($lnkPath)
    $sc.TargetPath = $exePath
    $sc.WorkingDirectory = $root
    $sc.Description = 'NillyMedia Lite — Ultra-Light Media Player & Voice Recorder | created by Nilly graphics'
    if (Test-Path $icoPath) { $sc.IconLocation = "$icoPath,0" }
    $sc.Save()
}
Write-Host '        OK -> Έτοιμες οι συντομεύσεις!' -ForegroundColor Green
Write-Host ''
Write-Host '  ΟΛΟΚΛΗΡΩΘΗΚΕ! Συντομεύσεις: Space (Play/Pause) · ←/→ (±5s) · F11 (Fullscreen) · P (Playlist) · M (Mute)' -ForegroundColor Cyan
Write-Host ''

if ($OpenDefaultApps) {
    Start-Process 'ms-settings:defaultapps'
} elseif (-not $Silent) {
    Start-Process -FilePath $exePath
}
