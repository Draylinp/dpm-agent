# Inicia la demo pública en esta computadora, lista para grabar el video (Windows, PowerShell).
# Uso, desde la carpeta del proyecto:
#   powershell -ExecutionPolicy Bypass -File scripts\iniciar-demo.ps1
# La clave de Meta se pide en pantalla (no se muestra ni se guarda en ningún archivo).
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'No se encuentra dotnet: instale el SDK de .NET 8 desde https://dotnet.microsoft.com/download/dotnet/8.0 y abra una ventana nueva.' -ForegroundColor Red
    exit 1
}
$sdks = @(dotnet --list-sdks 2>$null)
Write-Host ('SDK de .NET encontrados: ' + ($(if ($sdks.Count) { ($sdks | ForEach-Object { ($_ -split ' ')[0] }) -join ', ' } else { 'ninguno' })))
if (-not ($sdks | Where-Object { $_ -match '^8\.' })) {
    if ($sdks | Where-Object { $_ -match '^(9|1\d)\.' }) {
        # Un SDK más nuevo compila el proyecto; la app corre en el runtime más nuevo que haya.
        $env:DOTNET_ROLL_FORWARD = 'Major'
        Write-Host 'No hay SDK 8; se usará el más nuevo instalado.' -ForegroundColor Yellow
    } else {
        Write-Host 'Falta el SDK de .NET 8: instálelo desde https://dotnet.microsoft.com/download/dotnet/8.0 y abra una ventana nueva.' -ForegroundColor Red
        exit 1
    }
}

if (-not $env:META_API_KEY) {
    $segura = Read-Host 'Clave de la Meta Model API (Enter para seguir sin ella)' -AsSecureString
    $env:META_API_KEY = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($segura))
}
if (-not $env:META_API_KEY) {
    Write-Host 'Sin clave: el panel y el motor de agenda funcionan, pero el chat no podrá responder con el modelo.' -ForegroundColor Yellow
}

$puerto = 5199
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://localhost:$puerto"
$env:BaseDatos__Proveedor = 'Memoria'          # datos ficticios limpios en cada arranque
$env:Demo__Habilitado = 'true'
$env:Demo__ClavePanel = 'demo-video-2026'      # usuario "demo" del panel (clave pública de la demo)

Write-Host 'Compilando e iniciando la demo (la primera vez tarda un poco)...' -ForegroundColor Cyan
$app = Start-Process dotnet -ArgumentList @('run', '--project', 'src/DpmAgent.Api', '--no-launch-profile') -PassThru -NoNewWindow

$lista = $false
for ($i = 0; $i -lt 180 -and -not $lista; $i++) {
    Start-Sleep -Seconds 1
    try { $lista = (Invoke-WebRequest "http://localhost:$puerto/health/live" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { }
}
if (-not $lista) {
    Write-Host 'La app no respondió. Revise los mensajes de arriba.' -ForegroundColor Red
    exit 1
}

Start-Process "http://localhost:$puerto/"
Start-Process "http://localhost:$puerto/panel.html"
Write-Host ''
Write-Host "Demo:  http://localhost:$puerto/" -ForegroundColor Green
Write-Host "Panel: http://localhost:$puerto/panel.html   usuario: demo   clave: demo-video-2026" -ForegroundColor Green
Write-Host 'Para terminar, cierre esta ventana o presione Ctrl+C.'
Wait-Process -Id $app.Id
