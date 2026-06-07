# SteamHibernate

A **Steam-only** desktop tool that **cold-archives games you're not playing** with maximum compression and restores them on demand — so your system drive stops filling up and you don't have to delete-and-redownload.

> Status: **Plan 1 (Manual mode) is implemented and verified on real hardware.** Transparent auto-tiering (Plan 2, ProjFS) is on the roadmap. The project name is a working codename.

---

## Install

Download the latest **`SteamHibernate-Setup-x.y.z.exe`** from [Releases](https://github.com/BlackBearCC/SteamHibernate/releases) and run it. The installer is self-contained — it bundles the .NET runtime, `7za.exe`, and `precomp.exe`, so there are no prerequisites. It installs to Program Files with a Start Menu (and optional desktop) shortcut and an uninstaller.

By default, archives are stored **inside each game's own Steam library** (`<library>\SteamHibernate\<appid>`, same drive as the game). Since a verified Compress deletes the original folder, keeping the archive on the same drive nets a space saving rather than costing extra. Override with `ArchiveRoot` in config if you'd rather archive to another drive/NAS.

## What it does

Steam installs games as plain folders under `steamapps/common/<Game>` plus an `appmanifest_<appid>.acf`. SteamHibernate:

- **Scans** your Steam libraries (registry + `libraryfolders.vdf`), listing each game with size, last-played, and archive status.
- **Compress**: packs a game into a single archive (game files **+ its appmanifest +** a directory manifest + metadata header), then removes the original folder and appmanifest so **Steam shows the game as "not installed"** (it won't try to repair/redownload).
- **Restore**: extracts the archive back into place and restores the appmanifest, so **Steam instantly recognizes the game as installed — no redownload, no validation**. Restoring from a local archive is far faster than re-downloading 100 GB.

It talks to nothing external about your library; archives live wherever you point it (another drive, external SSD, NAS).

## Why not just "compress everything"

Modern game assets (textures, audio, video) ship **already compressed** (BC/DXT, Ogg, H.264, Unity LZ4/LZMA, UE Oodle). General-purpose compressors can't beat that entropy floor. Realistic savings:

| Game type | LZMA2 saving |
|---|---|
| Modern AAA / Unity / Oodle (assets pre-compressed) | ~10–35% |
| Older / indie / loosely-packed (uncompressed or zip/deflate data) | ~40–60% |

**The real win isn't shrinking a game to 10% — it's moving big games you're not playing off your system drive (100% reclaimed there), and getting them back in minutes instead of an hours-long redownload.**

## Compression engines

- **Default: 7-Zip LZMA2 (solid).** Robust, fast, good ratio.
- **Optional: precomp + LZMA2 (`.pc7z`), off by default.** `precomp` losslessly expands zlib/deflate streams so LZMA can recompress them, helping **deflate/zip-packed** games. ⚠️ It is **counterproductive on already-compressed games** (Unity/Oodle): measured on *Overcooked! 2* (Unity) — plain `5.4 GB / 4 min` vs precomp `5.9 GB / 10 min` (bigger **and** slower). Enable it per-game only when a game uses deflate packing.

Engines are pluggable; archives are self-identifying (`.7z` vs `.pc7z`) so a package always restores with the engine that created it. The fully-open default stack is precomp (open source) + 7-Zip/xz; `srep` is intentionally **not** bundled (it is closed-source freeware).

## Safety — never lose a game

Every state change is **commit-on-success**, never try-then-rollback:

- **Compress**: pack → verify integrity (and, for the precomp engine, a restore dry-run) → **only then** delete the original folder + appmanifest. Any failure leaves the original untouched. Metadata is committed before the original is removed, so there is no window where both copies can be lost.
- **Restore**: extract to a temp dir on the same volume → atomic move into place → restore appmanifest → clear the record. Any failure leaves the archive intact.

Corrupt config/metadata is surfaced, never silently reset.

## Requirements

- Windows 10 1809+ / Windows 11, NTFS.
- A 7-Zip executable (`7z`/`7za`). Point `SevenZipPath` at it, or have it on `PATH` / in `C:\Program Files\7-Zip`.
- To run from source: .NET 8 SDK. To run a published build: nothing (publish self-contained).
- (Optional) `precomp` for the `.pc7z` engine.

## Build & test

```bash
dotnet build
dotnet test                      # 7-Zip-dependent tests run if 7z is on PATH
PRECOMP_PATH=/path/to/precomp dotnet test   # also runs the precomp round-trip test

# Self-contained Windows build (no .NET install needed on the target):
dotnet publish src/SteamHibernate.App -c Release -r win-x64 --self-contained true -o out
```

## Usage

**GUI** — run `SteamHibernate.App` with no arguments: a game list with per-row **Compress / Restore** buttons and progress.

**Headless CLI** (scriptable, no display needed):

```
SteamHibernate.App.exe list                 # list installed + archived games
SteamHibernate.App.exe compress <appid>     # archive a game
SteamHibernate.App.exe restore  <appid>     # bring it back
```

## Configuration

`%AppData%\SteamHibernate\config.json`:

| Key | Meaning | Default |
|---|---|---|
| `ArchiveRoot` | Where archives are stored (empty = inside each game's Steam library) | empty (per-library) |
| `CompressionLevel` | LZMA2 level 1–9 | `9` |
| `SevenZipPath` | Path to 7-Zip exe | auto-detect |
| `EnablePrecomp` | Use the precomp engine | `false` |
| `PrecompPath` | Path to precomp exe | auto-detect |
| `IdleDays` | "Cold" threshold (for future auto-tiering) | `30` |
| `DefaultMode` | `Manual` / `Auto` | `Manual` |

## Notes & caveats

- **Do compress/restore with Steam closed**, or expect a one-time Steam Cloud "couldn't sync saves" prompt — changing game files under a running Steam confuses its cloud-save reconciliation. The game files are fine; just choose to proceed.
- **Anti-cheat games** (EAC/BattlEye/Vanguard) are best left alone with archive/restore; planned auto-tiering will exclude them.

## Roadmap

- **Plan 2 — Auto mode (ProjFS):** keep archived games appearing "installed" to Steam via a projected filesystem placeholder, auto-hydrate (extract) on first launch, auto-dehydrate when idle — so you never touch this tool, you just click Play. Gated on a spike validating that a ProjFS placeholder makes Steam show "Play" without triggering a redownload.
- Settings UI, precomp auto-detection of deflate-friendly games, package reclamation after restore.

## License

Not yet chosen — add a `LICENSE` file before distributing. The bundled compression stack (precomp, 7-Zip/xz) is open source; `srep` is deliberately not included.
