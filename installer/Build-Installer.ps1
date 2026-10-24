param([string]$CompilerPath, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content (Join-Path $root 'My Fancy Fences/My Fancy Fences.csproj')
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part project version.' }
if (-not $CompilerPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $CompilerPath = $command.Source }
    else { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
}
if (-not (Test-Path -LiteralPath $CompilerPath)) { throw 'Install Inno Setup 6.7 or pass -CompilerPath to ISCC.exe.' }
$setupOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'new-build' }
# The unpacked application stays next to the Setup EXE so the latest build can be tested without installing.
$appOutput = Join-Path $setupOutput 'My Fancy Fences'
if (Test-Path -LiteralPath $appOutput) {
    try { Remove-Item -LiteralPath $appOutput -Recurse -Force -ErrorAction Stop }
    catch { throw "Cannot clean '$appOutput'. Close the application running from this folder and build again. $($_.Exception.Message)" }
}
if (Test-Path -LiteralPath $setupOutput) { Get-ChildItem -LiteralPath $setupOutput -Filter 'My-Fancy-Fences-v*-Setup-win-x64.exe' | Remove-Item -Force }
& dotnet publish (Join-Path $root 'My Fancy Fences/My Fancy Fences.csproj') -c Release -r win-x64 --self-contained false -o $appOutput '-p:PublishSingleFile=false' '-p:DebugType=None' '-p:DebugSymbols=false'
if ($LASTEXITCODE -ne 0) { throw 'Application publishing failed.' }
& $CompilerPath "/DAppVersion=$version" "/DPublishDir=$appOutput" "/DInstallerOutputDir=$setupOutput" (Join-Path $PSScriptRoot 'MyFancyFences.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Get-Item (Join-Path $setupOutput "My-Fancy-Fences-v$version-Setup-win-x64.exe")
