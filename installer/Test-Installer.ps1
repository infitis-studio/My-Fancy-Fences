param([Parameter(Mandatory)][string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$testId = [guid]::NewGuid().ToString('N')
$testName = "My Fancy Fences Installer Test $testId"
$testRoot = Join-Path $root "build/installer-test-$testId"
$installDir = Join-Path $testRoot 'installed'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$payload = Get-Item -LiteralPath (Join-Path $root 'new-build/My Fancy Fences') -ErrorAction SilentlyContinue
if (-not $payload) { throw 'Build the installer first.' }

# Isolate the real setup logic from the user's installed app, shortcuts and autostart.
$source = Get-Content (Join-Path $PSScriptRoot 'MyFancyFences.iss') -Raw
$source = $source.Replace('AppId={{A2CC9A40-D764-4FE6-90D5-2734E1657C2B}', "AppId={{$testId}")
$source = $source.Replace('AppName=My Fancy Fences', "AppName=$testName")
$source = $source.Replace('SetupIconFile=..\My Fancy Fences\Assets\my-fancy-fences.ico', ('SetupIconFile=' + (Join-Path $root 'My Fancy Fences/Assets/my-fancy-fences.ico')))
$source = $source.Replace('Source: "Prepare-DotNet.ps1"', ('Source: "' + (Join-Path $PSScriptRoot 'Prepare-DotNet.ps1') + '"'))
$source = $source.Replace('Name: "{autoprograms}\My Fancy Fences"', ('Name: "{autoprograms}\' + $testName + '"'))
$source = $source.Replace('Name: "{autodesktop}\My Fancy Fences"', ('Name: "{autodesktop}\' + $testName + '"'))
$source = $source.Replace('My Fancy Fences.lnk', "$testName.lnk")
$source = $source.Replace('ValueName: "My Fancy Fences"', ('ValueName: "' + $testName + '"'))
$source = $source.Replace("'Run', 'My Fancy Fences'", "'Run', '$testName'")
$source = $source.Replace("CurrentVersion\Run', 'My Fancy Fences'", "CurrentVersion\Run', '$testName'")
$iss = Join-Path $testRoot 'test.iss'
$source | Set-Content -LiteralPath $iss -Encoding utf8BOM
& $CompilerPath '/Q' "/DPublishDir=$($payload.FullName)" "/DInstallerOutputDir=$testRoot" $iss
if ($LASTEXITCODE -ne 0) { throw 'Test installer compilation failed.' }
$setup = Get-ChildItem $testRoot -Filter '*Setup*.exe' | Select-Object -First 1
$runKey = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'
$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) "$testName.lnk"
$menuLink = Join-Path ([Environment]::GetFolderPath('Programs')) "$testName.lnk"
try {
    # An existing autostart selection must survive an installer update.
    Set-ItemProperty -LiteralPath $runKey -Name $testName -Value 'test-placeholder'
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /DIR="' + $installDir + '" /LOG="' + (Join-Path $testRoot 'install.log') + '"'
    $process = Start-Process -FilePath $setup.FullName -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Installation failed: $($process.ExitCode)" }
    foreach ($path in @('My Fancy Fences.exe', 'My Fancy Fences.dll', 'My Fancy Fences.runtimeconfig.json', 'unins000.exe', 'installer-managed')) {
        if (-not (Test-Path -LiteralPath (Join-Path $installDir $path))) { throw "Missing installed file: $path" }
    }
    if ((Get-Content (Join-Path $installDir 'installer-managed') -Raw) -ne 'currentuser') { throw 'Wrong installation mode.' }
    if (-not (Test-Path -LiteralPath $desktopLink) -or -not (Test-Path -LiteralPath $menuLink)) { throw 'Shortcuts were not created.' }
    $expectedRun = '"' + (Join-Path $installDir 'My Fancy Fences.exe') + '"'
    if ((Get-ItemPropertyValue -LiteralPath $runKey -Name $testName) -ne $expectedRun) { throw 'Autostart points at the wrong executable.' }
    $log = Get-Content (Join-Path $testRoot 'install.log') -Raw
    if ($log -match 'Extracting temporary file:.*Prepare-DotNet') { throw 'Setup downloaded .NET even though it is installed.' }
    $runtimeConfig = Get-Content (Join-Path $installDir 'My Fancy Fences.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (@($runtimeConfig.runtimeOptions.frameworks | Where-Object version -NE '10.0.0').Count) { throw 'Unexpected minimum runtime version.' }
    'PASS: installation, runtime skip, application files, desktop/Start menu shortcuts, autostart and installation mode.'
} finally {
    $uninstaller = Join-Path $installDir 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $process = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -PassThru -Wait
        if ($process.ExitCode -ne 0) { throw "Uninstall failed: $($process.ExitCode)" }
    } else {
        Remove-ItemProperty -LiteralPath $runKey -Name $testName -ErrorAction SilentlyContinue
    }
}
if ((Test-Path -LiteralPath $desktopLink) -or (Test-Path -LiteralPath $menuLink)) { throw 'Uninstall left a test shortcut behind.' }
if (Get-ItemProperty -LiteralPath $runKey -Name $testName -ErrorAction SilentlyContinue) { throw 'Uninstall left autostart behind.' }
if (Test-Path -LiteralPath (Join-Path $installDir 'installer-managed')) { throw 'Uninstall left the marker behind.' }
'PASS: uninstall removes installed files, shortcuts, autostart and marker.'

# Simulate a clean machine in the compiled wizard, without changing the host's runtime.
$missingSource = [regex]::Replace($source, '(?s)function DetectRuntime: String;.*?(?=function AutoStartSelected)', "function DetectRuntime: String;`nbegin`n  Result := '';`nend;`n`n")
$missingSource = $missingSource.Replace("  RuntimeProgress := CreateOutputProgressPage(CustomMessage('PreparingRuntime'), '');", @'
  if not RuntimeCheck.Checked or RuntimeCheck.Enabled then
    RaiseException('Missing runtime must be checked and locked.');
  Log('PASS: missing runtime is checked and locked.');
  RuntimeProgress := CreateOutputProgressPage(CustomMessage('PreparingRuntime'), '');
'@)
$missingSource = $missingSource.Replace("  if RuntimeDone then exit;", "  Result := 'Simulated missing-runtime test completed; no installation performed.';`n  exit;`n  if RuntimeDone then exit;")
$missingIss = Join-Path $testRoot 'missing-runtime.iss'
$missingSource | Set-Content -LiteralPath $missingIss -Encoding utf8BOM
$missingOutput = Join-Path $testRoot 'missing-runtime'
& $CompilerPath '/Q' "/DPublishDir=$($payload.FullName)" "/DInstallerOutputDir=$missingOutput" $missingIss
if ($LASTEXITCODE -ne 0) { throw 'Missing-runtime fixture compilation failed.' }
$missingSetup = Get-ChildItem $missingOutput -Filter '*Setup*.exe' | Select-Object -First 1
$missingLog = Join-Path $testRoot 'missing-runtime.log'
$arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /DIR="' + $installDir + '" /LOG="' + $missingLog + '"'
$process = Start-Process -FilePath $missingSetup.FullName -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 7 -or (Get-Content $missingLog -Raw) -notmatch 'PASS: missing runtime is checked and locked') {
    throw 'Missing-runtime wizard check failed.'
}
'PASS: missing runtime is checked and locked; the test stopped before downloading or installing anything.'
