<#
.SYNOPSIS
    Despliega PosVentas en el servidor (staging: pos-staging.hstech.com.ar).

.DESCRIPTION
    Se ejecuta desde la PC de desarrollo (Windows PowerShell 5.1 o PowerShell 7):
      1. git pull de main (exige árbol limpio y estar en main).
      2. Exige que la integración continua de GitHub haya pasado en verde para ESE commit
         (o corre los tests locales con -TestsLocales).
      3. dotnet publish de la API y del cliente Blazor, empaquetados como api/ y wwwroot/.
      4. Sube el paquete por SSH y lo activa con activar-release.sh, que reinicia la API, verifica
         API y cliente, y vuelve al release anterior si algo falla.
      5. Verifica la URL pública (cuando VerificarUrlPublica = true en deploy.settings.json).

    Compilar acá y no en el servidor evita que un Droplet chico se quede sin memoria compilando
    Blazor WebAssembly, y el servidor no necesita credenciales de GitHub.

    Los secretos NO pasan por este script: viven solo en el servidor, en
    /etc/posventas/posventas.env (root, permisos 600).

    Basado en el despliegue probado de ContratistaSaaS (deploy/deploy.ps1 de ese repositorio).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File deploy\deploy.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File deploy\deploy.ps1 -TestsLocales
#>
[CmdletBinding()]
param(
    # Corre los tests en esta PC en lugar de exigir la CI de GitHub en verde (requiere SQL Server local)
    [switch]$TestsLocales
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$config = Get-Content -Raw (Join-Path $PSScriptRoot 'deploy.settings.json') | ConvertFrom-Json
$llave = $config.LlaveSsh -replace '^~', $HOME
$destino = "$($config.UsuarioSsh)@$($config.Servidor)"
$opcionesSsh = @('-i', $llave, '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=20')

function Paso([string]$texto) { Write-Host "`n==> $texto" -ForegroundColor Cyan }

function Ejecutar([string]$programa, [string[]]$argumentos) {
    & $programa @argumentos
    if ($LASTEXITCODE -ne 0) { throw "Falló: $programa $($argumentos -join ' ') (código $LASTEXITCODE)" }
}

function ExigirCiEnVerde([string]$sha) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'No está la CLI de GitHub (gh) para verificar la CI. Instalala, o desplegá con -TestsLocales.'
    }
    $json = gh run list --commit $sha --workflow CI --limit 5 --json status,conclusion,event,url 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo consultar la CI en GitHub (¿gh auth login?). O desplegá con -TestsLocales.' }
    $corridas = @($json | ConvertFrom-Json)
    if ($corridas.Count -eq 0) { throw "La CI todavía no corrió para el commit $sha. Esperá a que termine." }
    $ok = $corridas | Where-Object { $_.status -eq 'completed' -and $_.conclusion -eq 'success' } | Select-Object -First 1
    if (-not $ok) {
        $estado = ($corridas | ForEach-Object { "$($_.status)/$($_.conclusion)" }) -join ', '
        throw "La CI del commit $sha no está en verde ($estado). No se despliega."
    }
    Write-Host "CI en verde para $sha ($($ok.url))"
}

Push-Location $raiz
try {
    if (-not (Test-Path $llave)) { throw "No se encontró la llave SSH $llave." }

    Paso "Actualizando el código ($($config.Rama))"
    $rama = (git rev-parse --abbrev-ref HEAD).Trim()
    if ($rama -ne $config.Rama) { throw "Estás en la rama '$rama'. Cambiá a '$($config.Rama)' para desplegar." }
    if (git status --porcelain) { throw 'Hay cambios sin commitear. Commiteá o descartá antes de desplegar.' }
    Ejecutar 'git' @('pull', '--ff-only', 'origin', $config.Rama)
    $sha = (git rev-parse HEAD).Trim()
    $commit = $sha.Substring(0, 7)

    if ($TestsLocales) {
        Paso 'Ejecutando las pruebas en esta PC'
        Ejecutar 'dotnet' @('test', $config.Solucion, '--nologo', '-v', 'q')
    }
    else {
        Paso 'Verificando la integración continua de GitHub'
        ExigirCiEnVerde $sha
    }

    $releaseId = (Get-Date -Format 'yyyyMMdd-HHmmss') + "-$commit"
    $temporal = Join-Path ([IO.Path]::GetTempPath()) "posventas-$releaseId"
    $publicacion = Join-Path $temporal 'release'
    $cliente = Join-Path $temporal 'cliente'
    $paquete = Join-Path $temporal 'release.tgz'

    Paso "Publicando la API (release $releaseId)"
    Ejecutar 'dotnet' @('publish', $config.ProyectoApi, '-c', 'Release', '-o', (Join-Path $publicacion 'api'), '--nologo')
    # La configuración de Desarrollo no viaja al servidor
    Remove-Item (Join-Path $publicacion 'api\appsettings.Development.json') -ErrorAction SilentlyContinue

    Paso 'Publicando el cliente Blazor'
    Ejecutar 'dotnet' @('publish', $config.ProyectoCliente, '-c', 'Release', '-o', $cliente, '--nologo')
    Copy-Item -Recurse (Join-Path $cliente 'wwwroot') (Join-Path $publicacion 'wwwroot')
    Remove-Item (Join-Path $publicacion 'wwwroot\appsettings.Development.json') -ErrorAction SilentlyContinue

    Ejecutar 'tar' @('-czf', $paquete, '-C', $publicacion, '.')
    Write-Host ("Paquete: {0:N1} MB" -f ((Get-Item $paquete).Length / 1MB))

    Paso 'Subiendo el paquete al servidor'
    $paqueteRemoto = "/tmp/posventas-$releaseId.tgz"
    $scriptRemoto = "/tmp/activar-release-$releaseId.sh"
    Ejecutar 'scp' (@('-P', $config.PuertoSsh) + $opcionesSsh + @($paquete, "${destino}:$paqueteRemoto"))
    Ejecutar 'scp' (@('-P', $config.PuertoSsh) + $opcionesSsh + @((Join-Path $PSScriptRoot 'activar-release.sh'), "${destino}:$scriptRemoto"))

    Paso 'Activando el release y verificando API y cliente'
    $comando = "sed -i 's/\r$//' $scriptRemoto && bash $scriptRemoto $releaseId $paqueteRemoto $($config.HostPublico); codigo=`$?; rm -f $scriptRemoto; exit `$codigo"
    Ejecutar 'ssh' (@('-p', $config.PuertoSsh) + $opcionesSsh + @($destino, $comando))

    if ($config.VerificarUrlPublica) {
        Paso "Verificando $($config.UrlPublica)"
        $respuesta = Invoke-WebRequest -Uri "$($config.UrlPublica)/health" -UseBasicParsing -TimeoutSec 30
        if ($respuesta.StatusCode -ne 200) { throw "La URL pública respondió $($respuesta.StatusCode)." }
    }
    else {
        Write-Host "`n(Sin verificación pública: VerificarUrlPublica = false hasta configurar Cloudflare.)" -ForegroundColor Yellow
    }

    Write-Host "`nDespliegue completo: release $releaseId activo en $($config.HostPublico)" -ForegroundColor Green
}
finally {
    if ($temporal -and (Test-Path $temporal)) { Remove-Item -Recurse -Force $temporal }
    Pop-Location
}
