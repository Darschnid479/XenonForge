$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$res = Join-Path $root 'XenonForge\Resources'
New-Item -ItemType Directory -Force -Path $res | Out-Null

# Pinned upstream data assets. XenonForge does NOT bundle or execute iso2god.exe.
$commit = '76207d7d7f89f551c7a5769560c0073e9ffe50e9'
$base = "https://raw.githubusercontent.com/iliazeus/iso2god-rs/$commit"

$assets = @(
    @{ Name = 'empty_live.bin'; Url = "$base/src/god/empty_live.bin"; Min = 45056; Max = 45056 },
    @{ Name = 'titles.jsonl'; Url = "$base/src/game_list/titles.jsonl"; Min = 500000; Max = 2000000 }
)

foreach ($asset in $assets) {
    $dest = Join-Path $res $asset.Name
    $need = -not (Test-Path $dest)
    if (-not $need) {
        $len = (Get-Item $dest).Length
        $need = $len -lt $asset.Min -or $len -gt $asset.Max
    }
    if ($need) {
        Write-Host "Downloading $($asset.Name) ..."
        Invoke-WebRequest -UseBasicParsing -Uri $asset.Url -OutFile $dest
    }
    $len = (Get-Item $dest).Length
    if ($len -lt $asset.Min -or $len -gt $asset.Max) {
        throw "Unexpected size for $($asset.Name): $len bytes"
    }
    $hash = (Get-FileHash -Algorithm SHA256 $dest).Hash
    Write-Host "OK  $($asset.Name)  $len bytes  SHA256 $hash"
}
