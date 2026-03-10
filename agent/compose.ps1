# Run docker compose for the agent. From repo root:
#   .\agent\compose.ps1 up -d              # main (64-bit)
#   $env:BUILD_ARM32=1; .\agent\compose.ps1 up -d   # Raspberry Pi 32-bit

$ErrorActionPreference = "Stop"
$agentDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $agentDir
Push-Location $rootDir
try {
    $arm32 = $env:BUILD_ARM32
    if ($arm32 -eq "1" -or $arm32 -eq "true" -or $arm32 -eq "yes") {
        docker compose -f agent/docker-compose.yml -f agent/docker-compose.arm32.yml @args
    } else {
        docker compose -f agent/docker-compose.yml @args
    }
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
