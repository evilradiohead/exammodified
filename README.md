# CHSBPlus — recovered source

Reconstructed from `TestPlatformasi-2.1.1-x64.exe` by decompiling the shipped .NET
assemblies (ILSpy). This is the lost source, recovered — not hand-written.

## What this is

A .NET 10 **WPF kiosk / exam app** (Uzbek test platform, home `https://chsb.piima.uz/`).
It opens the site in an embedded WebView2 and enforces a locked-down exam environment
(process guard, keyboard/mouse blocking, camera proctoring via OpenCV/YuNet).

## Layout

```
CHSBPlus/            WPF app (WinExe) — UI, kiosk/security, proctoring, WebView2
  ChsbKiosk/           App, MainWindow, ProcessGuard, KeyboardBlocker, MouseBlocker,
                       KioskConfig, ExitPasswordDialog, Logger, WindowsPolicyRestorer
  ChsbKiosk.Views/     LoginView, TestsView, ExamView, SurveyView, FinishedView (.cs + .xaml)
  ChsbKiosk.Proctor/   CameraProctor, FaceAnalyzer
  ChsbKiosk.Security/  DeviceIdentity, DpapiProtector
  ChsbKiosk.Update/    AppUpdater
  ChsbKiosk.Rendering/ QuestionRenderer
  themes/styles.xaml   shared styles/brushes
  assets/, *.ttf, app.ico, question.html, yunet.onnx   embedded resources
ChsbKiosk.Core/      logic library (no UI)
  Api/     ApiClient + all DTOs (login, tests, exam, survey, proctor, update)
  Exam/    ExamSession, AnswerSync, ExamClock
  Proctor/ face detection (YuNet decode), proctor policy/signals
  Storage/ LocalStateStore, DPAPI data protector
  Update/  UpdateVerifier
config.example.json  default kiosk config (home URL, blocked processes, camera policy…)
```

96 `.cs` files + 9 `.xaml`, ~10,200 lines.

## Fidelity notes (decompiled, so read before building)

- **Logic / C#**: near-original. Readable, idiomatic. Comments and original local
  variable names are gone; everything else (types, methods, records, constants) is intact.
- **XAML**: recovered from BAML — structurally faithful (layout, styles, bindings,
  named elements, event handlers), though not byte-identical formatting.
- **Lost**: code comments, `#region`s, original file partitioning (one file per type now).

## To rebuild — the `.csproj` files are now fixed and should build

The two `.csproj` files have been rewritten to build cleanly (NuGet packages instead of the
extracted DLLs, project reference, WPF items wired, correct `Resource` vs `EmbeddedResource`).

1. **Build on Windows** with the **.NET 10 SDK** (it's WPF + DPAPI + WebView2 — Windows only).
   ```
   dotnet restore CHSBPlus/CHSBPlus.csproj
   dotnet build  CHSBPlus/CHSBPlus.csproj -c Release
   ```
   Building `CHSBPlus` pulls in `ChsbKiosk.Core` via project reference. (Opening `CHSBPlus.csproj`
   in Visual Studio works too.)
2. What was fixed, for reference:
   - **Packages** (real NuGet, versions float — pin them once restore picks a version):
     `Microsoft.Web.WebView2`, `OpenCvSharp4.Windows` (camera/face proctoring, bundles the native
     runtime), `System.Security.Cryptography.ProtectedData` (DPAPI). All `System.*` / WPF / Registry
     references were dropped — the SDK and the `net10.0-windows` target supply them.
   - **Project reference**: `CHSBPlus` → `ChsbKiosk.Core` (was a DLL hint path).
   - **XAML items**: `ChsbKiosk.App.xaml` is the `ApplicationDefinition`; the rest are `Page`
     (default XAML globbing is off because the files have non-standard names).
   - **Resources**: `logo.ico`, the two `logo-mark*.png`, and the Onest fonts are `Resource`
     (XAML pack URIs); `question.html`, the three `font-*.ttf`, and `yunet.onnx` stay
     `EmbeddedResource` with their `LogicalName` (read by code).
   - **Target**: `net10.0-windows10.0.17763.0`, x64 (matches the original; enables Registry + WinRT).

If `restore` complains about a package version, let it tell you the nearest available one and pin it.

### Already verified / fixed (ChsbKiosk.Core compiles clean on .NET 10)

`ChsbKiosk.Core` (plain `net10.0`) was compiled on macOS with the .NET 10 SDK — **0 errors,
0 warnings**. Three decompiler artifacts were fixed along the way (they were in Core, which the
app references, so they'd have blocked the Windows build too):
- `LangVersion` was `15.0` (invalid for the C# 14 compiler) → `latest`, in both projects.
- `UpdateVerifier.cs` used `[GeneratedRegex]` methods *with bodies* (SYSLIB1043) → replaced with
  cached runtime `Regex`; the generated `System.Text.RegularExpressions.Generated/` files removed.
- `ExamSession.OptionKeys` referenced a synthesized `<>z__ReadOnlyArray<>` type → replaced with a
  plain `string[]` (arrays already implement `IReadOnlyList<T>`).
- Added `<NoWarn>` for the decompiler's harmless nullable warnings in both projects.

The WPF app (`CHSBPlus`) can only be compiled on Windows; it was scanned for the same artifact
classes and is clean of them, but a first Windows build may still surface a stray decompiler quirk —
paste any errors and they're quick to fix.

## Practice build (new) — lockdown off + hidden AI answer hint

A second, separate app built from this same source, gated behind the `PRACTICE`
compile symbol. The exam build is byte-for-byte unaffected — none of the code below
is compiled into it.

Build it:
```
dotnet build CHSBPlus/CHSBPlus.csproj -c Release -p:Practice=true -o build/practice
```
A normal build (no `-p:Practice=true`) is the exam app. Note the output is still named
`CHSBPlus.exe` (the assembly name is kept — XAML hardcodes `/CHSBPlus;component/...` pack URIs,
so renaming the assembly would break fonts/icons). Build it into its own folder as shown; if you
want a distinct filename, rename the produced `CHSBPlus.exe` to `CHSBPlus.Practice.exe` — renaming
the *file* is safe, the internal assembly name is what the pack URIs use.

What `PRACTICE` changes:
- **Lockdown off** — no keyboard/mouse hooks, no process guard, normal maximized window
  that can be minimized/closed/alt-tabbed (see `App.cs`, `MainWindow.cs`, `#if !PRACTICE`).
  Students can research freely and use other apps.
- **Hidden AI answer hint** — the screen is identical to the exam. Pressing `[` while a
  question is shown silently asks Claude for the best answer; when it replies (~1–2s) that
  option's text just turns **bold**. No card, no label, no icon. Press `[` again to **hide** it
  (toggle). Nothing of this is sent to your server.
- **Monitoring kept on (watch, don't block)** — unlike the lockdown, the *observing* parts stay:
  - **Heartbeat** runs as normal (app-alive telemetry).
  - **Camera proctoring + snapshots + incident/violation reports** are enabled via
    `cameraPolicy: "auto"` in the practice config (face presence, looking-away, multiple faces →
    snapshots to your server). This still requires the server to mark the attempt as *proctored*;
    if practice attempts come back un-proctored, the camera won't start (a small `#if PRACTICE`
    override could force it — not done yet).
  - **Focus / tab-switch reporting** — when a student switches away, the app sends a
    `KIOSK_FOCUS_LOST` signal to your server, but (unlike the exam) does **not** force the window
    back. Report without enforce (`MainWindow.Window_Deactivated`, `#if PRACTICE`).

Config (`config.practice.json` → rename to `config.json` next to the exe):
- `aiApiKey` — your institution's Anthropic API key. **Use a dedicated key with a spend cap**;
  anyone with the exe + config can read it.
- `aiModel` — defaults to `claude-opus-5-5`; `claude-haiku-5-5` is far cheaper and fine for
  picking one of four options.
- `cameraPolicy` — `"auto"` keeps camera monitoring on; set `"never"` to turn it off.

Honest limits:
- The bold = **the AI's pick**, not a verified answer key (your server never sends the real
  answer). Usually right; can be wrong on tricky questions.
- **Images are supported**: any `<img>` in the question or options is downloaded through the
  app's authenticated client (same-origin only, ≤6 MB each, ≤8 images) and sent to Claude's
  vision model alongside the text. If an image can't be fetched, it's skipped and the text still
  goes through. Data-URI images are ignored (the text path still runs).

Where the code lives:
- `CHSBPlus/ChsbKiosk.Assist/AnswerAssistant.cs` — the Claude call + answer parsing (`#if PRACTICE`).
- `CHSBPlus/ChsbKiosk.Rendering/QuestionRenderer.cs` — `[`→`assist`→`Reveal` wiring (`#if PRACTICE`).
- `CHSBPlus/question.html` — the `[` key, the `reveal` message, and the bold style.
- `CHSBPlus/ChsbKiosk/KioskConfig.cs` — `AiApiKey` / `AiModel` (`#if PRACTICE`).

Still Windows-only to build/run (WPF + Win32), and the csproj reference cleanup above applies.

## Build in the cloud (GitHub Actions) — no Windows machine needed

WPF only builds on Windows, so to build from a Mac, push this folder to GitHub and let the
included workflow (`.github/workflows/build.yml`) compile it on a Windows runner. It produces
two self-contained single-file `.exe`s (no .NET needed on the target PCs), matching how the
original shipped.

One-time push (run inside `CHSBPlus-recovered/`):
```
git init
git add .
git commit -m "CHSBPlus recovered + practice build"
gh repo create chsbplus --private --source=. --push    # or add a remote manually and push
```
Then on github.com → the repo → **Actions** → the latest run → **Artifacts**:
- `CHSBPlus-exam` — the exam app (lockdown on, no AI).
- `CHSBPlus-practice` — the practice app (lockdown off, hidden `[` AI hint).

Every push rebuilds both. Put the practice build's config (fill `aiApiKey` in
`config.practice.json`, rename to `config.json`) next to `CHSBPlus.exe` on the practice PCs.

## Tests

`tests/AnswerFormat.Tests/` is a small cross-platform (net10.0) test that exercises the **real**
AI-hint logic — now factored into `ChsbKiosk.Core/ChsbKiosk.Core.Assist/AnswerFormat.cs`
(request-JSON building, answer-letter parsing, HTML→text, image-URL extraction), which the WPF
`AnswerAssistant` calls. It runs on any OS (no Windows needed) and the CI `test` job runs it on
every push:
```
dotnet run --project tests/AnswerFormat.Tests/AnswerFormat.Tests.csproj -c Release
```
It guards the wire format (that the Anthropic request keeps snake_case keys like `max_tokens` /
`media_type`), the multimodal body shape, and refusal/parse handling.

## How it was recovered (reproducible)

- Outer `.exe` = .NET single-file installer `CHSBPlusInstaller`, with a ZIP appended
  (`app/CHSBPlus.exe` + WebView2 runtime installers + `config.example.json`).
- Managed assemblies were extracted from the .NET single-file bundle, then decompiled:
  `ilspycmd CHSBPlus.dll -p --decompile-baml --referencepath <framework-dlls>`.
