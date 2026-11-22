# AGENTS.md

VideoDuplicateFinder (VDF) — cross-platform duplicate video/image finder. C# / .NET 10 (`net10.0`, no `global.json`, default branch `master`). All frontends share one core library.

## Layout

- `VDF.Core/` — all real logic: scan engine (`ScanEngine*.cs`), `DatabaseWrapper.cs` (SQLite `ScannedFiles.db`), FFmpeg/FFprobe engines (`FFTools/`, incl. native FFmpeg 8.x binding via FFmpeg.AutoGen), pHash, Chromaprint audio fingerprinting (`Chromaprint/`), optional AI matching (`AI/` — ONNX Runtime + DINOv2 embeddings).
- `VDF.GUI/` Avalonia 12 desktop app. `VDF.CLI/` console app (assembly name `vdf-cli`). `VDF.Web/` Blazor Server web UI. These are thin frontends; put shared logic in `VDF.Core`, which has `InternalsVisibleTo` for all of them.
- `VDF.TestSupport/` — shared test models, `TestVideoGenerator` (generates synthetic media via ffmpeg CLI), and `tiny-embedder.onnx` as a download-free stand-in for the DINOv2 model.
- `FakeDatabaseGenerator/` — dev tool that writes a fake `ScannedFiles.db` for UI work. `VDF.Benchmarks/` — BenchmarkDotNet.

## Build & test

- Build: `dotnet build VideoDuplicateFinder.sln`. Requires the .NET 10 SDK.
- Tests are xUnit in five separate projects; run them individually:
  - `dotnet test VDF.Core.Tests`, `VDF.CLI.Tests`, `VDF.GUI.Tests`, `VDF.Web.Tests` — no external deps (Core.Tests brings the full `Microsoft.ML.OnnxRuntime` package so AI tests run without downloads).
  - `dotnet test VDF.IntegrationTests` — **requires ffmpeg on PATH** (`choco install ffmpeg -y` on Windows). It generates fixtures at runtime via `TestVideoGenerator`. Without ffmpeg it fails hard when `CI` is set, otherwise tests skip (`Xunit.SkippableFact`). The `[Collection("Ffmpeg")]` fixture is shared per run.
- Release builds publish GUI and CLI as **Native AOT** (`-p:PublishAot=true -r <rid>`); Web is JIT/self-contained (Blazor Server is not AOT-safe). Debug/Release builds also set `PlatformTargets=x64;arm64`.

## Package/version pins that must stay consistent

- Version is fixed at `4.1.0` in `Directory.Build.props` and never bumped — the git commit (baked into assembly metadata) is the real build id. The release tag `4.1.x` in `.github/workflows/releases.yml` must stay aligned; `VDF.GUI.Tests/VersionConsistencyTests.cs` enforces it. `4.0.x` is frozen, never touch it.
- ONNX Runtime is pinned to `1.23.2` and must match in three places: `Microsoft.ML.OnnxRuntime.Managed` in `VDF.Core.csproj`, the full `Microsoft.ML.OnnxRuntime` in `Core.Tests`/`IntegrationTests`, and `AiComponents.RuntimeVersion`. `AiComponents.cs` also pins the model SHA256 (runtime downloads go to `{StateFolder}/ai`, nothing is bundled).
- `FFmpeg.AutoGen` `8.1.0` in Core. The auto-downloader (`FfmpegDownloader.cs`) fetches matching BtbN/yt-dlp shared builds tagged by major version.
- In `VDF.GUI.csproj`: **SkiaSharp is deliberately pinned to `4.151.1` above Avalonia's transitive 3.x** (finalizer race, issues #902/#830). Keep both SkiaSharp lines on the same version; do not "fix" it.

## Conventions

- Every `.cs` file starts with the AGPLv3 header block (see `.editorconfig` `file_header_template`: `Copyright (C) 2026 0x90d`). New files must include it.
- Indentation: tabs for C#/XAML, 2 spaces for `.yml` and `.csproj`/XML (see `.editorconfig`).
- Two workflows matter: PRs run `.github/workflows/dotnetcore.yml` (build + all test projects incl. integration); pushes to `master` run `releases.yml` (rebuilds and **replaces** the daily-build assets on the `4.1.x` tag) and `docker.yml` (publishes `ghcr.io/0x90d/vdf-web`, multi-arch).
- Contributing rules (README): one PR per fix/feature — do not merge additions into one PR; describe what the PR does unless it references an existing issue; open an issue first for larger PRs.