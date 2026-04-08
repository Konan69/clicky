# Clicky - Agent Instructions

<!-- This is the single source of truth for all AI coding agents. -->

## Overview

Windows tray companion app. Lives in the notification area instead of a normal main window. Clicking the tray icon opens a custom floating panel. Holding `Ctrl + Alt` starts push-to-talk, streams microphone audio to AssemblyAI, captures screenshots from every connected display, sends transcript + screenshots to Claude through the Cloudflare Worker, then plays the response through ElevenLabs. Claude can append `[POINT:x,y:label:screenN]` tags and the transparent overlay moves Clicky’s cursor bubble to that point.

`main` is the Windows rewrite. The legacy macOS Swift app was moved to `macos-swift-main`.

## Architecture

- **App Type**: Tray-only Windows desktop app, no taskbar-first main window flow
- **Framework**: C# + .NET 8 + WPF, with Win32 and WinForms interop where Windows APIs are the right tool
- **Pattern**: MVVM with `ObservableObject` view models and a central `CompanionCoordinator`
- **AI Chat**: Claude via Cloudflare Worker SSE streaming
- **Speech-to-Text**: AssemblyAI universal streaming websocket with short-lived worker-issued tokens
- **Text-to-Speech**: ElevenLabs via Cloudflare Worker
- **Screen Capture**: Per-display Windows desktop capture using GDI (`Graphics.CopyFromScreen`)
- **Voice Input**: `NAudio` microphone capture at 16kHz PCM16 mono
- **Push-To-Talk**: Global low-level keyboard hook for `Ctrl + Alt`
- **Element Pointing**: Claude emits `[POINT:x,y:label:screenN]` tags; the overlay maps screenshot coordinates back onto the virtual desktop
- **Concurrency**: async/await for all network and streaming work, Dispatcher-bound UI state updates

### API Proxy

The desktop app never calls Anthropic, AssemblyAI, or ElevenLabs directly with raw credentials. All secrets stay in the Worker at `worker/src/index.ts`.

| Route | Upstream | Purpose |
|-------|----------|---------|
| `POST /chat` | `api.anthropic.com/v1/messages` | Claude screenshot + transcript chat |
| `POST /tts` | `api.elevenlabs.io/v1/text-to-speech/{voiceId}` | ElevenLabs audio |
| `POST /transcribe-token` | `streaming.assemblyai.com/v3/token` | Short-lived AssemblyAI websocket token |

Worker secrets: `ANTHROPIC_API_KEY`, `ASSEMBLYAI_API_KEY`, `ELEVENLABS_API_KEY`

Worker vars: `ELEVENLABS_VOICE_ID`

## Key Architecture Decisions

**Tray-first shell**: The app uses a `NotifyIcon` host instead of a normal startup window. WPF owns the panel and overlay windows, but the tray icon itself comes from WinForms because it is the simplest reliable Windows tray API.

**Transparent overlay**: The overlay is a borderless WPF window with Win32 extended styles for layered, transparent, click-through behavior. That keeps the bubble visible without blocking desktop input.

**Low-level push-to-talk hook**: `RegisterHotKey` is not enough for modifier hold/release behavior. The app uses `WH_KEYBOARD_LL` so `Ctrl + Alt` behaves like real push-to-talk.

**AssemblyAI turn finalization**: Releasing push-to-talk sends `ForceEndpoint` and waits briefly for AssemblyAI’s formatted turn. If that explicit final turn does not arrive in time, the app falls back to the best partial transcript it already has.

**Screenshot-to-point mapping**: Screenshots are captured per display in original pixel size. That lets the overlay convert Claude’s `[POINT:x,y:label:screenN]` coordinates back into virtual desktop coordinates without guessing.

## Key Files

| File | Lines | Purpose |
|------|-------|---------|
| `src/Clicky.App/AppBootstrapper.cs` | ~151 | App composition root. Wires tray shell, overlay, coordinator, microphone capture, transcription, and worker clients. |
| `src/Clicky.App/Services/CompanionCoordinator.cs` | ~435 | Central state machine. Owns push-to-talk lifecycle, screenshot capture, Claude streaming, TTS playback, overlay state, and conversation history. |
| `src/Clicky.App/Views/CompanionPanelWindow.xaml` | ~183 | Floating tray panel UI. Shows state, prompt box, model picker, worker summary, latest transcript, and latest response. |
| `src/Clicky.App/Views/OverlayWindow.xaml` | ~60 | Transparent Clicky overlay bubble and live mic level bar. |
| `src/Clicky.App/Services/Input/GlobalPushToTalkHook.cs` | ~117 | Global `Ctrl + Alt` low-level keyboard hook. Publishes press and release transitions. |
| `src/Clicky.App/Services/Audio/WindowsMicrophoneCaptureService.cs` | ~77 | `NAudio` microphone capture at AssemblyAI’s preferred PCM16 mono format. Emits audio chunks and normalized audio level. |
| `src/Clicky.App/Services/Transcription/AssemblyAiStreamingTranscriptionClient.cs` | ~62 | Fetches worker-issued AssemblyAI websocket tokens and starts streaming sessions. |
| `src/Clicky.App/Services/Transcription/AssemblyAiStreamingTranscriptionSession.cs` | ~397 | Websocket session implementation. Streams PCM audio, tracks live turns, finalizes transcripts, and handles fallback delivery. |
| `src/Clicky.App/Services/ScreenCapture/WindowsScreenCaptureService.cs` | ~40 | Captures all attached displays as PNG bytes for Claude vision requests. |
| `src/Clicky.App/Services/Chat/ClaudeWorkerChatClient.cs` | ~188 | Streams Claude SSE responses through the Worker and packages screenshot payloads. |
| `src/Clicky.App/Services/Tts/ElevenLabsTtsClient.cs` | ~152 | Fetches ElevenLabs audio from the Worker and plays it with `MediaPlayer`. |
| `src/Clicky.App/Services/Chat/PointerInstructionParser.cs` | ~35 | Parses Claude point tags and strips them from spoken/displayed copy. |
| `worker/src/index.ts` | ~141 | Cloudflare Worker proxy for `/chat`, `/tts`, and `/transcribe-token`. |

## Build & Run

Windows only:

```bash
dotnet restore Clicky.sln
dotnet run --project src/Clicky.App/Clicky.App.csproj
```

You can also open `Clicky.sln` in Visual Studio and run `Clicky.App`.

## Cloudflare Worker

```bash
cd worker
npm install
npx wrangler secret put ANTHROPIC_API_KEY
npx wrangler secret put ASSEMBLYAI_API_KEY
npx wrangler secret put ELEVENLABS_API_KEY
npx wrangler deploy
```

For local worker dev:

```bash
cd worker
npx wrangler dev
```

Set the desktop app Worker URL in `src/Clicky.App/appsettings.json` or `CLICKY_WORKER_BASE_URL`.

## Code Style & Conventions

### Variable and Method Naming

- Be extremely clear and specific
- Optimize for clarity over concision
- Use longer names when they remove ambiguity
- Keep argument names aligned with the original variable names

### Code Clarity

- Clear beats clever
- Prefer more lines if they make the behavior easier to read
- Add comments only when the why is not obvious from the code

### C# / WPF Conventions

- Use WPF for app UI
- Use Win32 or WinForms interop only when Windows shell behavior requires it
- Keep UI state mutations on the WPF Dispatcher
- Use async/await for streaming, network, and long-running operations
- Any interactive control should explicitly communicate clickability
- Buttons should use a hand cursor on hover

### Do NOT

- Do not reintroduce the old macOS project on `main`
- Do not add direct API calls that bypass the Worker
- Do not ship secrets in the desktop app
- Do not replace the global low-level hook with `RegisterHotKey` for push-to-talk
- Do not add unrelated platforms or compatibility branches inside this branch

## Git Workflow

- Branch naming: `feature/description` or `fix/description`
- Commit messages: imperative mood, concise, explain the why
- Do not force-push to `main`

## Self-Update Instructions

When you make architecture changes on this branch, update this file.

1. Add new source files to the key files table when they matter architecturally.
2. Remove files that no longer exist.
3. Update architecture notes when platform decisions or core flows change.
4. Update build commands when the Windows build flow changes.
5. Update conventions if the user sets new coding rules.
