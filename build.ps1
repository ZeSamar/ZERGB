# Сборка ZERGB.exe из исходников.
# Требует: Windows + .NET Framework 4.8 (csc.exe), NAudio.dll в lib/.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) {
    throw "csc.exe не найден — установите .NET Framework 4.8 Developer Pack."
}

$refs = @(
    "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll",
    "$env:WINDIR\Microsoft.NET\assembly\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll",
    "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll",
    "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll",
    "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Management\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Management.dll",
    "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Windows.Forms\v4.0_4.0.0.0__b77a5c561934e089\System.Windows.Forms.dll",
    "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Drawing\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Drawing.dll"
)

$args = @(
    '/nologo', '/noconfig', '/target:winexe',
    '/out:ZERGB.exe',
    '/win32icon:icon.ico',
    '/resource:ui.xaml', '/resource:wheel.png', '/resource:icon.ico',
    '/resource:NAudio.dll', '/r:NAudio.dll',
    '/codepage:65001'
) + ($refs | ForEach-Object { "/r:$_" }) + @(
    '/r:System.dll', '/r:System.Core.dll', '/r:mscorlib.dll',
    'WidgetApp.cs'
)

Push-Location $root
try {
    & $csc @args
    if ($LASTEXITCODE -ne 0) { throw "csc завершился с кодом $LASTEXITCODE" }
} finally {
    Pop-Location
}

Write-Host "Готово: $root\ZERGB.exe" -ForegroundColor Green
