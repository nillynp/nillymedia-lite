# ============================================================================
# NillyMedia Lite — Clean Uninstaller (Zero Admin · HKCU)
# created by Nilly graphics (Sotiris Christos) — https://nillygraphics.com/
# ============================================================================
param([switch]$Silent)

$ErrorActionPreference = 'SilentlyContinue'
Write-Host ''
Write-Host '  Αφαίρεση εγγραφών & συντομεύσεων του NillyMedia Lite...' -ForegroundColor Yellow

Stop-Process -Name 'NillyMediaLite' -Force -ErrorAction SilentlyContinue

Remove-Item -Path 'HKCU:\Software\Classes\NillyMediaLite.Media' -Recurse -Force
Remove-Item -Path 'HKCU:\Software\Classes\Applications\NillyMediaLite.exe' -Recurse -Force
Remove-Item -Path 'HKCU:\Software\NillyGraphics\NillyMediaLite' -Recurse -Force
Remove-ItemProperty -Path 'HKCU:\Software\RegisteredApplications' -Name 'NillyMediaLite' -Force

@('.mp4','.m4v','.mov','.wmv','.avi','.mkv','.webm','.mp3','.wav','.m4a','.aac','.wma','.flac','.ogg') | ForEach-Object {
    $owp = "HKCU:\Software\Classes\$_\OpenWithProgids"
    if (Test-Path $owp) { Remove-ItemProperty -Path $owp -Name 'NillyMediaLite.Media' -Force -ErrorAction SilentlyContinue }
}

$desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'NillyMedia Lite.lnk'
$startLnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Nilly graphics\NillyMedia Lite.lnk'
if (Test-Path $desktopLnk) { Remove-Item $desktopLnk -Force }
if (Test-Path $startLnk) { Remove-Item $startLnk -Force }

Write-Host '  OK -> Αφαιρέθηκαν καθαρά όλες οι εγγραφές & συντομεύσεις του NillyMedia Lite.' -ForegroundColor Green
Write-Host ''
if (-not $Silent) { Read-Host '  Πάτησε Enter για κλείσιμο' }
