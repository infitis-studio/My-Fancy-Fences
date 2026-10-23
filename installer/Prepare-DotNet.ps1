param(
    [Parameter(Mandatory)][string]$Destination,
    [string]$RepairVersion = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

try {
    if ($RepairVersion -and $RepairVersion -notmatch '^10\.0\.\d+$') {
        throw 'Invalid .NET Desktop Runtime version.'
    }
    $metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
    $runtimes = @($metadata.releases | ForEach-Object { $_.windowsdesktop } |
        Where-Object { $_.version -match '^10\.0\.\d+$' })
    if ($RepairVersion) {
        $runtime = $runtimes | Where-Object version -EQ $RepairVersion | Select-Object -First 1
    } else {
        $runtime = $runtimes | Sort-Object { [version]$_.version } -Descending | Select-Object -First 1
    }
    if (-not $runtime) { throw 'The requested .NET Desktop Runtime installer was not found at Microsoft.' }
    $asset = $runtime.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.exe' } | Select-Object -First 1
    if (-not $asset -or $asset.hash -notmatch '^[a-fA-F0-9]{128}$') { throw 'Invalid Microsoft runtime metadata.' }
    $uri = [uri]$asset.url
    if ($uri.Scheme -ne 'https' -or $uri.Host -notin @('builds.dotnet.microsoft.com', 'download.visualstudio.microsoft.com')) {
        throw 'Unexpected runtime download address.'
    }
    Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $Destination
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA512).Hash -ne $asset.hash) {
        throw 'The downloaded runtime failed its SHA-512 integrity check.'
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $Destination
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') {
        throw 'The downloaded runtime does not have a valid Microsoft signature.'
    }
    exit 0
} catch {
    $_.Exception.Message | Set-Content -LiteralPath "$Destination.error.txt" -Encoding UTF8
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
    exit 1
}
