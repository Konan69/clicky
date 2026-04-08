# Clicky for Windows

Clicky is an AI desktop companion that lives in the Windows tray, sees your screens, listens while you hold `Ctrl + Alt`, streams your transcript and screenshots to Claude, speaks back through ElevenLabs, and can point at UI elements with a floating overlay.

`main` is now the Windows C# rewrite. The old macOS Swift app is preserved on the `macos-swift-main` branch.

![Clicky demo](clicky-demo.gif)

## What ships on `main`

- Tray-first Windows app built with `C#`, `.NET 8`, and `WPF`
- Floating companion panel instead of a normal main window
- Transparent always-on-top overlay for Clicky’s cursor bubble
- Global push-to-talk with `Ctrl + Alt`
- Live microphone streaming to AssemblyAI over websocket
- Multi-monitor screenshot capture before each Claude request
- Claude SSE streaming through the Cloudflare Worker
- ElevenLabs playback through the Cloudflare Worker
- `[POINT:x,y:label:screenN]` parsing so Claude can move the overlay cursor

## Prereqs

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 or `dotnet` CLI
- Node.js 18+ for the Worker
- Cloudflare account
- Anthropic, AssemblyAI, and ElevenLabs API keys

WPF is Windows-only. Do not expect this branch to build on macOS or Linux.

## 1. Set up the Worker

```bash
cd worker
npm install
npx wrangler secret put ANTHROPIC_API_KEY
npx wrangler secret put ASSEMBLYAI_API_KEY
npx wrangler secret put ELEVENLABS_API_KEY
```

Set your ElevenLabs voice id in `worker/wrangler.toml`:

```toml
[vars]
ELEVENLABS_VOICE_ID = "your-voice-id-here"
```

Deploy it:

```bash
npx wrangler deploy
```

For local worker dev:

```bash
cd worker
npx wrangler dev
```

Create `worker/.dev.vars` with:

```bash
ANTHROPIC_API_KEY=...
ASSEMBLYAI_API_KEY=...
ELEVENLABS_API_KEY=...
ELEVENLABS_VOICE_ID=...
```

## 2. Point the app at your Worker

Set the Worker URL in either place:

- `src/Clicky.App/appsettings.json`
- `CLICKY_WORKER_BASE_URL` env var

Example:

```json
{
  "workerBaseUrl": "https://your-worker-name.your-subdomain.workers.dev",
  "defaultClaudeModel": "claude-sonnet-4-6"
}
```

## 3. Run the Windows app

From Windows:

```bash
dotnet restore Clicky.sln
dotnet run --project src/Clicky.App/Clicky.App.csproj
```

Or open `Clicky.sln` in Visual Studio and run `Clicky.App`.

The app lives in the tray. Left-click the tray icon to open the panel. Hold `Ctrl + Alt` to talk.

## Project layout

```text
src/Clicky.App/               Windows WPF app
  Services/CompanionCoordinator.cs
  Services/Audio/
  Services/Transcription/
  Services/Chat/
  Services/Tts/
  Views/
  ViewModels/
worker/                       Cloudflare proxy
  src/index.ts
AGENTS.md                     Architecture + agent notes
```

## Notes

- The app never ships raw API keys. Everything goes through the Worker.
- The overlay is click-through and doesn’t steal input.
- The current screenshot capture uses Windows GDI capture per display.
- If you want the original Mac version, switch to `macos-swift-main`.
