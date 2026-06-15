<h1 align="center">Video Duplicate Finder</h1>

<p align="center">
  <strong>Find duplicate videos & images — even with different resolution, frame rate, or watermarks.</strong>
</p>

<p align="center">
  <a href="https://github.com/0x90d/videoduplicatefinder/releases/tag/4.0.x"><img src="https://img.shields.io/badge/download-latest%20build-blue" alt="Download"></a>
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-green" alt="Platform">
  <img src="https://img.shields.io/badge/license-AGPLv3-orange" alt="License">
</p>

---

**Video Duplicate Finder (VDF)** is a cross-platform tool that detects duplicate video and image files based on content similarity. Unlike typical duplicate finders that rely on file hashes, VDF analyzes the actual visual (and optionally audio) content — so it catches duplicates even when they differ in resolution, frame rate, encoding, or have watermarks applied.

## Highlights

- **Content-aware matching** — finds visually similar videos/images, not just byte-identical files
- **Partial clip detection** — discovers when a short clip is part of a longer video (via audio fingerprinting)
- **Three interfaces** — Desktop GUI, headless CLI, and browser-based Web UI
- **Cross-platform** — Windows, Linux, macOS
- **Fast scanning** — native FFmpeg bindings for maximum speed; ultra-fast incremental rescans
- **pHash support** — optional perceptual hashing for image deduplication at zero extra cost
- **Docker-ready** — one-command deployment with FFmpeg bundled in the image
- **REST API** — full programmatic access with JWT and API key authentication

---

## Download

[**Latest Daily Build**](https://github.com/0x90d/videoduplicatefinder/releases/tag/4.0.x) — rebuilt automatically on every commit.

| Package | Description |
|---------|-------------|
| `GUI-<platform>` | Desktop application |
| `CLI-<platform>` | Command-line tool |
| `Web-<platform>` | Self-contained web server |

> **Upgrading from 3.x?** Your scan database migrates automatically on first load. Cached image hashes are recomputed on the next scan; video hashes are unaffected. Downgrading after migration is not recommended. The last 3.x build is available on the [3.0.x release](https://github.com/0x90d/videoduplicatefinder/releases/tag/3.0.x).

---

## Quick Start

### Desktop GUI

**Requirements:** FFmpeg & FFprobe (auto-downloaded on first launch; native binding requires FFmpeg 8.x shared libraries).

| Platform | Install FFmpeg | Run |
|----------|---------------|-----|
| **Windows** | Download from [ffmpeg.org](https://ffmpeg.org/download.html), place `ffmpeg.exe` & `ffprobe.exe` alongside `VDF.GUI.exe` or on `PATH` | Double-click `VDF.GUI.exe` |
| **Linux** | `sudo apt-get install ffmpeg` | `chmod +x VDF.GUI && ./VDF.GUI` |
| **macOS** | `brew install ffmpeg` | Open `Video Duplicate Finder.app` |

<details>
<summary>Linux: add to application menu</summary>

The archive includes `videoduplicatefinder.desktop` and `icon.png`:

```bash
sed -i "s|/opt/videoduplicatefinder|$(pwd)|g" videoduplicatefinder.desktop
mkdir -p ~/.local/share/applications
cp videoduplicatefinder.desktop ~/.local/share/applications/
```
</details>

<details>
<summary>macOS: bypass Gatekeeper</summary>

If macOS blocks the app, right-click the `.app` and choose **Open**.

If it still refuses (macOS 14+ / Tahoe):
```bash
xattr -cr "Video Duplicate Finder.app"
codesign --force --deep --sign - "Video Duplicate Finder.app"
```
</details>

### CLI

Download `CLI-<platform>` from the [releases page](https://github.com/0x90d/videoduplicatefinder/releases/tag/4.0.x).

```bash
# Basic scan
vdf-cli scan-and-compare --include /path/to/media

# Multiple directories, JSON output
vdf-cli scan-and-compare \
  --include /mnt/movies \
  --include /mnt/series \
  --exclude /mnt/movies/extras \
  --format json \
  --output results.json

# Auto-mark lowest quality, dry run first
vdf-cli scan-and-compare --include /mnt/media --action lowest-quality --dry-run
```

**Common options:**

| Flag | Description | Default |
|------|-------------|---------|
| `--include <path>` | Directory to scan (repeatable) | required |
| `--exclude <path>` | Directory to exclude (repeatable) | — |
| `--threshold <n>` | Hash difference threshold | 5 |
| `--percent <n>` | Minimum similarity % to report | 96 |
| `--parallelism <n>` | Parallel hashing threads | 1 |
| `--include-images` | Also scan image files | off |
| `--use-phash` | Use perceptual hashing | off |
| `--partial-clip-detection` | Enable partial clip detection | off |
| `--format json\|text\|csv` | Output format | text |
| `--output <file>` | Write results to file | stdout |

**Auto-delete strategies (`--action`):**

| Strategy | Keeps |
|----------|-------|
| `lowest-quality` | Highest bitrate/resolution per group |
| `smallest-file` | Largest file per group |
| `shortest-duration` | Longest duration per group |
| `worst-resolution` | Highest resolution per group |
| `100-percent-only` | Only acts on 100% identical groups |

> Always review with `--dry-run` before deleting.

### Docker (Web UI)

The fastest way to get started — FFmpeg is included, no extra installation needed.

```bash
docker run -d \
  --name vdf-web \
  -p 8080:8080 \
  -v vdf-db:/root/.config/VDF \
  -v vdf-state:/root/.local/state/VDF \
  -v /path/to/your/media:/media:ro \
  ghcr.io/0x90d/vdf-web:latest
```

Open **http://localhost:8080** and enter the password shown by `docker logs vdf-web`.

<details>
<summary>Docker Compose (recommended for permanent installs)</summary>

1. Download [`docker-compose.yml`](docker-compose.yml) from this repo.
2. Edit the file — add your media mounts and optionally set a password:
   ```yaml
   environment:
     - VDF_WEB_PASSWORD=mysecretpassword
   volumes:
     - /mnt/nas/movies:/mnt/nas/movies:ro
     - /mnt/nas/series:/mnt/nas/series:ro
   ```
3. Start: `docker compose up -d`
4. Update: `docker compose pull && docker compose up -d`
</details>

**Environment variables:**

| Variable | Description |
|----------|-------------|
| `VDF_WEB_PASSWORD` | Custom password (auto-generated if unset) |
| `VDF_WEB_AUTH=false` | Disable authentication |
| `VDF_API_KEYS` | Comma-separated API keys (`X-API-Key` header) |
| `VDF_BASE_PATH` | Sub-path for reverse proxy (e.g. `/vdf`) |
| `VDF_CORS_ORIGINS` | Comma-separated CORS origins |
| `VDF_TLS_CERT` / `VDF_TLS_KEY` | TLS certificate/key paths for HTTPS |

**Volumes:**

| Volume | Purpose |
|--------|---------|
| `/root/.config/VDF` | Settings & credentials (persist across updates) |
| `/root/.local/state/VDF` | Scan database (persist across updates) |
| Your media paths | Mount media directories (read-only recommended) |

> Images available for `linux/amd64` and `linux/arm64` (Raspberry Pi / NAS). Published to [GHCR](https://github.com/0x90d/videoduplicatefinder/pkgs/container/vdf-web) and updated on every commit.

---

## Web UI (Standalone)

For headless machines and NAS devices without Docker.

Download `Web-<platform>` from the [releases page](https://github.com/0x90d/videoduplicatefinder/releases/tag/4.0.x), then:

```bash
# Linux/macOS
chmod +x VDF.Web && ./VDF.Web

# Windows
VDF.Web.exe
```

Open **http://localhost:5000** and enter the auto-generated password from the console.

Change the port: `ASPNETCORE_URLS=http://+:8080 ./VDF.Web`

> **Security:** The Web UI is password-protected but intended for local/Docker use only. Do not expose it to the internet.

---

## Partial Clip Detection

VDF can detect when a shorter video is a partial clip of a longer one — a scene ripped from a movie, a clip saved from a longer recording, etc. It uses audio fingerprinting (Chromaprint-style chroma extraction + sliding-window Hamming matching) to find candidates that the visual scan misses, then optionally confirms each match by comparing frames at the matched offset.

This runs as an **optional second phase** after the visual scan. Matched pairs appear with a **Clip Offset** column showing where in the source the clip starts.

### Enabling

In **Settings → Partial Clip Detection**, check **Enable Partial Clip Detection**:

| Setting | Default | Description |
|---------|---------|-------------|
| Min clip / source ratio (%) | 10 | Minimum clip duration as % of source duration |
| Min audio similarity (%) | 80 | Minimum Hamming similarity for fingerprint match |
| Require visual confirmation | on | Reject audio matches that don't also look similar |
| Min visual similarity (%) | 85 | Minimum frame similarity for visual confirmation |

> Requires audio tracks in both files. Videos without audio are skipped.

---

## REST API

The Web UI backend exposes a full REST API. Swagger UI is available at `/swagger` in Development mode.

**Authentication:** JWT Bearer token (`POST /api/auth/login`) or API key (`X-API-Key` header).

| Group | Prefix | Description |
|-------|--------|-------------|
| Auth | `/api/auth/` | Login, refresh, logout, status |
| Scan | `/api/scan/` | Start, stop, pause, resume, progress |
| Results | `/api/results/` | List duplicates, delete/move/link items |
| Settings | `/api/settings/` | Get/update settings |
| Thumbnails | `/api/thumbnails/` | Retrieve scan thumbnails |
| SSE | `/api/sse/` | Real-time progress events |
| Health | `/health` | Health check (no auth) |

```bash
# Login and start a scan
TOKEN=$(curl -s -X POST http://localhost:8080/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"password":"mysecretpassword"}' | jq -r '.access_token')

curl -X POST http://localhost:8080/api/scan/start \
  -H "Authorization: Bearer $TOKEN"

# Or use an API key
curl -X POST http://localhost:8080/api/scan/start \
  -H "X-API-Key: my-api-key"
```

---

## Screenshots

<img src="https://user-images.githubusercontent.com/46010672/129763067-8855a538-4a4f-4831-ac42-938eae9343bd.png" width="510">

---

## Building

- .NET 10.x
- Visual Studio 2022 or later recommended

## Contributing

- One pull request per addition or fix — don't merge multiple changes into one PR
- Describe what the PR does (unless it references an existing issue)
- For larger changes, open an issue for discussion first

## License

AGPLv3

## Credits

- [Avalonia](https://github.com/AvaloniaUI/Avalonia) — Desktop GUI framework
- [ActiPro Avalonia Controls](https://github.com/Actipro/Avalonia-Controls) — UI controls
- [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen) — Native FFmpeg bindings
- [MemoryPack](https://github.com/Cysharp/MemoryPack) — High-performance serialization
- [React](https://react.dev/) + [Vite](https://vitejs.dev/) + [TypeScript](https://www.typescriptlang.org/) — Web UI frontend
- [ASP.NET Core](https://learn.microsoft.com/aspnet/core/) — Web backend
- [Swashbuckle](https://github.com/domaindrivendev/Swashbuckle.AspNetCore) — OpenAPI/Swagger UI
- [SignalR](https://learn.microsoft.com/aspnet/core/signalr/) — Real-time scan progress
- [AcoustID.NET](https://github.com/wo80/AcoustID.NET) — Audio fingerprinting pipeline for partial clip detection (LGPL 2.1)
