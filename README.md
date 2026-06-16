# Book of the End

**Book of the End** is a polished, fully offline Windows data-recovery application. It scans storage devices, locates deleted or lost files, lets you preview them, and recovers them safely to a destination of your choice. All processing is local — there is no cloud dependency and no network access.

## Features

- **Drive detection** — Automatically lists every mounted volume (internal/external HDD & SSD, USB flash drives, memory cards) with drive letter, label, capacity, used/free space, file system, bus type, and internal/external classification (via `DriveInfo` + WMI).
- **Quick Scan** — Fast recovery of recently deleted files from the **Recycle Bin** (`$I`/`$R` pairs) and from **deleted NTFS Master File Table** records, with original names, paths, and timestamps intact.
- **Deep Scan** — Sector-level **file signature carving** of the raw volume to recover files no longer referenced by the file system (e.g. after a format). Supports JPG, PNG, GIF, BMP, TIFF, PDF, ZIP/Office, RAR, 7Z, MP4, AVI, MKV, WAV, FLAC, MP3, and more.
- **Searchable / sortable results** — Filter by name, type, or path; sort any column. Each result shows type, size, recovery status, estimated quality, last-modified date, and original path.
- **Preview before recovery** — Image thumbnails/full view, text files, PDF (via WebView2), and audio/video playback (via `MediaElement`).
- **Safe recovery** — Recover single files, multiple files, or whole folder structures to a custom destination. The app **refuses to write back to the source drive by default** and warns you if you try.
- **Metadata preservation** — Restores the original filename, extension, folder structure, and timestamps when available; generates a safe replacement name and clearly flags items when metadata was lost.
- **Performance** — Multi-threaded background scanning, real-time progress, estimated time remaining, and pause / resume / cancel.
- **Theming** — Light Mode (white + purple) and Dark Mode (black + gold), switchable at runtime.
- **Logging & reports** — Session logs and per-run recovery reports under `%LOCALAPPDATA%\BookOfTheEnd`.

## Requirements

- Windows 10 (build 19041+) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (to build) / .NET 8 Desktop Runtime (to run)
- **Administrator privileges** — raw volume access (`\\.\C:`) and MFT parsing require elevation. The app manifest requests this automatically.
- WebView2 runtime — installed automatically by `Setup.exe` if missing (needed for PDF preview only)

## Build & Run

```powershell
# From the repository root
dotnet build

# Run (a UAC prompt will appear because raw disk access needs admin)
dotnet run --project src/BookOfTheEnd
```

To produce a self-contained executable:

```powershell
dotnet publish src/BookOfTheEnd -c Release -r win-x64 --self-contained false -o publish
```

Then launch `publish/BookOfTheEnd.exe` (accept the UAC elevation prompt).

## Windows installer (MSI + Setup.exe)

A proper **Windows installer** is included under `installer/`. It installs to Program Files, adds Start Menu and Desktop shortcuts, and registers an entry in **Settings → Apps → Installed apps** for uninstall.

**What to share with users:** `dist/BookOfTheEnd-2.4.1-x64-Setup.exe` — a single setup program that:
- Installs **Microsoft Edge WebView2 Runtime** automatically if it is missing (needed for PDF preview)
- Then installs Book of the End (self-contained — no separate .NET install required)

**Prerequisites to build the installer:** [.NET 8 SDK](https://dotnet.microsoft.com/download) and [WiX Toolset 5+](https://wixtoolset.org/) (`winget install WiXToolset.WiX`).

```powershell
# From the repository root — publishes, downloads WebView2 bootstrapper, builds MSI + Setup.exe
.\build\build-installer.ps1
```

Output:
- `dist/BookOfTheEnd-2.4.1-x64-Setup.exe` (~65 MB) — **recommended for distribution**
- `dist/BookOfTheEnd-2.4.1-x64.msi` — MSI only (advanced / IT deployment)

**Install on a PC:** double-click `Setup.exe` and follow the wizard (administrator approval required).

**Code signing (recommended for public release):** Unsigned installers trigger Windows SmartScreen ("Unknown publisher"). To sign builds, set one of:

```powershell
# Certificate in your Windows cert store (e.g. from DigiCert, Sectigo, SSL.com)
$env:CODESIGN_CERT_THUMBPRINT = "YOUR_SHA1_THUMBPRINT"
.\build\build-installer.ps1

# Or a PFX file
$env:CODESIGN_PFX_PATH = "C:\certs\codesign.pfx"
$env:CODESIGN_PFX_PASSWORD = "your-password"
.\build\build-installer.ps1
```

Signing requires `signtool.exe` (Windows SDK / Visual Studio Build Tools). Purchase an **Authenticode code signing certificate** from a trusted CA (~$200–400/year for standard; EV certs reduce SmartScreen delays further).

**Silent install (optional):**

```powershell
.\dist\BookOfTheEnd-2.4.1-x64-Setup.exe /quiet
```

**Uninstall:** Settings → Apps → Installed apps → Book of the End → Uninstall.

## How to use

1. Launch the app (accept the administrator prompt).
2. Select a drive from **Connected Drives**.
3. Choose **Quick Scan** (fast, recently deleted) or **Deep Scan** (thorough, carves raw sectors), optionally narrowing the file-type filters.
4. Press **Start Scan**. Watch progress, ETA, and live results. Pause/resume/cancel anytime.
5. Click a result to **preview** it.
6. Select files and press **Recover Selected** (or **Recover All**), then choose a destination **on a different drive**.

## Project structure

```
src/BookOfTheEnd/
  Interop/        Raw volume access (P/Invoke, sector-aligned reader)
  Models/         Domain models and enums
  Services/
    Ntfs/         Boot sector, MFT records, data runs, volume reader
    RecycleBin/   $I/$R parser
    Carving/      File signatures + Deep Scan carver
    Scanning/     Quick/Deep scan engines, pause/resume controller
    *             Drive detection, recovery, preview, theming, logging
  ViewModels/     MVVM (CommunityToolkit.Mvvm)
  Views/          Disclaimer dialog
  Themes/         Light, Dark, and shared control styles
  MainWindow.*    Main UI + preview rendering
```

## Disclaimer

Recovery success depends on the physical condition of the drive and whether the deleted data has already been overwritten. Not all deleted or formatted files can be recovered, and original filenames/metadata may not always be recoverable. For the best results, stop using the affected drive immediately and always recover to a **different** drive.
