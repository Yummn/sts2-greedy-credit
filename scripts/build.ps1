param(
    [string]$Sts2Path = "",
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$LocalDotnet = Join-Path $Root ".tools\dotnet\dotnet.exe"
if(Test-Path $LocalDotnet){
    $env:DOTNET_ROOT = Split-Path -Parent $LocalDotnet
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}

function Get-DotnetMajor {
    $sdks = (& dotnet --list-sdks 2>$null) -split "`n" | ForEach-Object { ($_ -split '\s+')[0] } | Where-Object { $_ }
    $majors = foreach($s in $sdks){ [int](($s -split '\.')[0]) }
    if($majors){ return ($majors | Measure-Object -Maximum).Maximum }
    return 0
}

if(-not (Get-Command dotnet -ErrorAction SilentlyContinue)){
    throw "dotnet not found. Install .NET SDK 9+ first."
}
if((Get-DotnetMajor) -lt 9){
    throw "Need .NET SDK 9+. Current SDKs:`n$(dotnet --list-sdks)"
}

$props = @()
if($Sts2Path){ $props += "/p:Sts2Path=$Sts2Path" }

dotnet restore .\GreedyCredit.csproj @props
if($LASTEXITCODE -ne 0){ exit $LASTEXITCODE }

dotnet build .\GreedyCredit.csproj -c $Configuration @props
if($LASTEXITCODE -ne 0){ exit $LASTEXITCODE }

Write-Host "Build finished. If Sts2Path was valid, files were copied to <Sts2Path>/mods/GreedyCredit/." -ForegroundColor Green
