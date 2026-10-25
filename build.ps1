$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\FtpNative\FtpNative.csproj'
$output = Join-Path $root 'dist'

Write-Host 'Publicando FtpNative (exe unico, self-contained)...' -ForegroundColor Cyan
& dotnet publish $project -c Release -o $output -r win-x64 --self-contained true /p:PublishSingleFile=true /p:DebugType=None /p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "Falha na publicacao."
}

$exe = Join-Path $output 'FtpNative.exe'
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Pronto: $exe ($size MB)" -ForegroundColor Green
Write-Host "Instalar:  $exe --install        (execute como administrador)" -ForegroundColor Yellow
Write-Host "Interface: $exe" -ForegroundColor Yellow
