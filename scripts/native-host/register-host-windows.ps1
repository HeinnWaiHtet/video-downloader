param(
  [string]$InstallDir = "$env:LOCALAPPDATA\\AuthorizedDownloader",
  [string]$ExtensionId = "__EXTENSION_ID__"
)

$HostName = "com.authorized.downloader.host"
$ManifestDir = Join-Path $InstallDir "native-host"
$ExePath = Join-Path $InstallDir "Downloader.Host.exe"
$PublishDir = Resolve-Path ./Downloader.Host/bin/Release/net8.0/publish -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force -Path $ManifestDir | Out-Null
if (-not $PublishDir) {
  throw "Publish output not found. Run: dotnet publish ./Downloader.Host -c Release"
}

Copy-Item -Recurse -Force "$PublishDir/*" $InstallDir

$manifestPath = Join-Path $ManifestDir "$HostName.json"
$manifest = @{
  name = $HostName
  description = "Authorized downloader native host"
  path = $ExePath
  type = "stdio"
  allowed_origins = @("chrome-extension://$ExtensionId/")
} | ConvertTo-Json -Depth 4

Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8

$chromeKey = "HKCU:\\Software\\Google\\Chrome\\NativeMessagingHosts\\$HostName"
$edgeKey = "HKCU:\\Software\\Microsoft\\Edge\\NativeMessagingHosts\\$HostName"

New-Item -Force -Path $chromeKey | Out-Null
Set-ItemProperty -Path $chromeKey -Name "(Default)" -Value $manifestPath

New-Item -Force -Path $edgeKey | Out-Null
Set-ItemProperty -Path $edgeKey -Name "(Default)" -Value $manifestPath

Write-Host "Registered native host for Chrome and Edge."
