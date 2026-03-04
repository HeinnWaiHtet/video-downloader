# Authorized Video Downloader (IDM-style)

This repository implements an IDM-style downloader for **authorized media only**.

## Components

- `BrowserExtension/` (Chrome/Edge MV3)
  - Injects a Download button on supported pages.
  - Uses native messaging to call local C# host.
- `Downloader.Host/` (native messaging host)
  - Receives `detect/startDownload/cancel` requests.
  - Routes through adapter + compliance + hybrid engine.
- `Downloader.Core/`
  - Contracts, adapters, engines, IPC types, compliance policy.
- `Downloader.UI/`
  - Local desktop web GUI (modern flex layout) for URL/quality/folder/name/download workflow.
- `Downloader.Desktop/`
  - Native .NET MAUI desktop app (non-web UI) for macOS (Mac Catalyst) and Windows.
- `Downloader.Tests/`
  - Offline self-tests for core behaviors.

## Runtime and LTS note

The requested target is .NET 10 LTS. The current workspace has .NET 8 SDK available, so this implementation is built as `net8.0` and structured for straightforward upgrade to `net10.0` once SDK is installed.

## Build

```bash
dotnet build Downloader.sln
```

## Run host

```bash
dotnet run --project Downloader.Host
```

## Run desktop GUI (no extension required)

```bash
dotnet run --project Downloader.UI
```

Then open:

```text
http://127.0.0.1:5077
```

Enable verbose ASP.NET request logs for dev debugging:

```bash
UI_DEBUG=1 dotnet run --project Downloader.UI
```

## Deploy web UI to Vercel

Important:
- This downloader backend depends on `yt-dlp`, local file writes, and long-running jobs.
- Vercel serverless is not a good fit for full download processing.
- Use Vercel for UI/demo hosting only, or host backend on a VM/container platform (Render/Railway/Fly.io) and point UI there.

Basic Vercel upload steps:

1. Push this repo to GitHub.
2. In Vercel: `Add New Project` -> import repo.
3. Set root directory to `Downloader.UI`.
4. Build command:

```bash
dotnet publish -c Release
```

5. Output directory:

```text
bin/Release/net8.0/publish
```

6. Environment variable:

```text
UI_DEBUG=0
```

CLI alternative:

```bash
npm i -g vercel
cd /Users/ayechanmay/Documents/Playground/Downloader.UI
vercel
```

## Run native desktop app (non-web)

Install MAUI workloads first:

```bash
dotnet workload restore /Users/ayechanmay/Documents/Playground/Downloader.Desktop/Downloader.Desktop.csproj
```

Then run:

```bash
dotnet build /Users/ayechanmay/Documents/Playground/Downloader.Desktop/Downloader.Desktop.csproj -f net8.0-maccatalyst
```

On Windows:

```bash
dotnet build /Users/ayechanmay/Documents/Playground/Downloader.Desktop/Downloader.Desktop.csproj -f net8.0-windows10.0.19041.0
```

## Run tests

```bash
dotnet run --project Downloader.Tests
```

## Extension setup

1. Open `chrome://extensions` or `edge://extensions`.
2. Enable Developer mode.
3. Load unpacked extension from `BrowserExtension/`.
4. Build host in Release and register native host:
   - macOS: `scripts/native-host/register-host-macos.sh`
   - Windows: `scripts/native-host/register-host-windows.ps1`
5. Replace `__EXTENSION_ID__` in manifest registration scripts with your actual extension ID.

## Supported sites in this MVP

- YouTube (first-class)
- Facebook (initial adapter)

## Compliance

- DRM/protected content is blocked.
- Unsupported bypass-required downloads are blocked.
- Site allowlist enforced in host policy (`youtube`, `facebook`).

## Known limitations

- `yt-dlp` must be installed and discoverable on PATH for fallback engine.
- Options-page site toggles are stored but not yet enforced by content-script filtering logic.
- Windows folder picker in `Downloader.Desktop` is not wired yet; manual folder path entry works.
