# Shiny.AiConversation

A centralized AI service library for .NET MAUI apps that orchestrates chat, speech recognition, wake word detection, text-to-speech, and persistent message history into a single `IAiConversationService` interface.

[![NuGet](https://img.shields.io/nuget/v/Shiny.AiConversation.svg)](https://www.nuget.org/packages/Shiny.AiConversation/)

## Features

- **Chat Integration** — Send text or voice messages to any AI backend via [Microsoft.Extensions.AI](https://devblogs.microsoft.com/dotnet/introducing-microsoft-extensions-ai/)
- **Wake Word Detection** — Hands-free activation with continuous keyword listening
- **Speech-to-Text / Text-to-Speech** — Full voice loop powered by [Shiny.Speech](https://github.com/shinyorg/speech)
- **Acknowledgement Modes** — None, AudioBlip (sound effects), LessWordy (concise TTS), or Full (complete TTS)
- **Persistent Chat History** — Pluggable `IMessageStore` for storing and querying past conversations
- **AI History Lookup Tool** — Optional `AITool` that lets the AI search past conversations on its own
- **State Management** — Observable `AiState` (Idle / Listening / Thinking / Responding) with events
- **Sound Effects** — Configurable sound stream factories for each state transition

## TODO
- Voice interruption (stop TTS when user starts talking OR an additional "Hey Copilot" wake word to interrupt)
    - Quiet words?  Stop, cancel, enough, silence, shut up
    - Interruption Mode - any words, wake words, specific words?
      - Stop words - stop, cancel, shut up, nevermind, quiet, silence, enough
- Sessions - ability to start different AI sessions based on time passed
- Acknowledgement sounds is present, but we need acknowledgement "Hi User" or "What can I help you with?"
  - Manually activation could just be a sound?  Maybe only the wake word should have a greeting, and manual text input doesn't need it?

- Speech to text - Wait for anything? then 2 seconds of silence
- 
## Installation

```bash
dotnet add package Shiny.AiConversation
```

## Quick Start

### 1. Register the service

```csharp
using Shiny.AiConversation;

var builder = MauiApp.CreateBuilder();
builder
    .UseMauiApp<App>()
    .ConfigureFonts(fonts => { ... });

// Register an IChatClient in DI (from any Microsoft.Extensions.AI-compatible provider)
builder.Services.AddChatClient(new OpenAIClient("your-api-key").GetChatClient("gpt-4o").AsIChatClient());

builder.Services.AddShinyAiConversation(opts =>
{
    // Optional — enable persistent history + AI lookup tool
    opts.SetMessageStore<MyMessageStore>(addAiLookupTool: true);
});

var app = builder.Build();

// Configure after build
var ai = app.Services.GetRequiredService<IAiConversationService>();
ai.SystemPrompts.Add("You are a helpful assistant.");
ai.SoundResolver = name => FileSystem.OpenAppPackageFileAsync(name);
ai.OkSound = "ok.mp3";
ai.ThinkSound = "think.mp3";
ai.RespondingSound = "responding.mp3";
ai.ErrorSound = "error.mp3";
ai.CancelSound = "cancel.mp3";

return app;
```

### 2. Chat Client Setup

By default, the library resolves `IChatClient` from DI. Simply register your chat client with the container:

```csharp
builder.Services.AddChatClient(new OpenAIClient("your-api-key").GetChatClient("gpt-4o").AsIChatClient());
```

#### Shiny.AiConversation.OpenAi

A ready-made static OpenAI provider. Works with any OpenAI-compatible endpoint (OpenAI, Azure OpenAI, Ollama, etc.).

```bash
dotnet add package Shiny.AiConversation.OpenAi
```

```csharp
builder.Services.AddShinyAiConversation(opts =>
{
    opts.AddStaticOpenAIChatClient(
        apiToken: "your-api-key",
        endpointUri: "https://api.openai.com/v1",
        modelName: "gpt-4o"
    );
});
```

#### Shiny.AiConversation.Maui.GithubCopilot

A MAUI-specific provider that authenticates via the GitHub device code flow and uses the Copilot API. Tokens are stored in `SecureStorage`. Authentication is fully self-contained — the library shows a popup with the device code, copies it to the clipboard, and opens the browser for the user to authorize.

```bash
dotnet add package Shiny.AiConversation.Maui.GithubCopilot
```

```csharp
builder.Services.AddShinyAiConversation(opts =>
{
    opts.AddGithubCopilotChatClient();
});
```

No additional setup is needed — the provider handles the entire OAuth flow, token exchange, caching, and re-authentication on expiry.

#### Custom Provider

For other backends, implement `IChatClientProvider`:

```csharp
using Microsoft.Extensions.AI;
using Shiny.AiConversation;

public class MyChatClientProvider : IChatClientProvider
{
    public async Task<IChatClient> GetChatClient(CancellationToken cancelToken = default)
    {
        // Handle auth, token refresh, etc.
        return BuildChatClient();
    }
}

// Register it:
builder.Services.AddShinyAiConversation(opts =>
{
    opts.SetChatClientProvider<MyChatClientProvider>();
});
```

### 3. Implement `IMessageStore` (optional)

Provide persistent storage for chat history. Without this, `GetChatHistory` and `ClearChatHistory` will throw.

```csharp
using Shiny.AiConversation;

public class MyMessageStore : IMessageStore
{
    public Task Store(ChatMessage chatMessage, CancellationToken cancellationToken)
    {
        // Persist user or assistant ChatMessage
    }

    public Task Store(string? userTriggeringMessage, ChatResponse response, CancellationToken cancellationToken)
    {
        // Persist the complete AI response
    }

    public Task Clear(DateTimeOffset? beforeDate = null)
    {
        // Clear all or messages before the given date
    }

    public Task<IReadOnlyList<AiChatMessage>> Query(
        string? messageContains = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        // Query with optional filters, return ordered by timestamp
    }
}
```

### 4. Use the service

```csharp
public class ChatViewModel(IAiConversationService aiService) : ObservableObject
{
    public async Task SendMessage(string text)
    {
        await aiService.TalkTo(text, CancellationToken.None);
    }

    public async Task UseMicrophone()
    {
        await aiService.ListenAndTalk(CancellationToken.None);
    }

    public async Task StartWakeWord()
    {
        await aiService.StartWakeWord("Hey Assistant");
    }
}
```

## API Overview

### IAiConversationService

| Member | Description |
|--------|-------------|
| `TalkTo(string, CancellationToken)` | Send a text message to the AI |
| `ListenAndTalk(CancellationToken)` | Capture speech via microphone and send to AI |
| `StartWakeWord(string)` | Begin continuous wake word detection |
| `StopWakeWord()` | Stop wake word detection |
| `GetChatHistory(...)` | Query persisted chat history with optional filters |
| `ClearChatHistory(...)` | Clear persisted history (all or before a date) |
| `ClearCurrentChat()` | Clear in-memory session messages |
| `Status` | Current `AiState` (Idle / Listening / Thinking / Responding) |
| `Acknowledgement` | Get/set the response delivery mode |
| `SystemPrompts` | System prompts prepended to every request |
| `StatusChanged` | Event fired with the new `AiState` on any state change |
| `AiResponded` | Event fired when the AI completes a response with `AiResponse` (Response, WasReadAloud) |

### Acknowledgement Modes

| Mode | Behavior |
|------|----------|
| `None` | No audio feedback or text-to-speech |
| `AudioBlip` | Short sound effects at state transitions |
| `LessWordy` | Text-to-speech with a "be concise" system prompt |
| `Full` | Text-to-speech with full unmodified responses |

### ChatLookupAITool

When `SetMessageStore<T>(addAiLookupTool: true)` is used, the library registers an `AITool` named `lookup_chat_history`. This allows the AI to autonomously search past conversations when the user asks about previous discussions — no extra code required.

## Architecture

```
┌─────────────────────────────────────────────────┐
│                   IAiConversationService                     │
│  (orchestrates chat, speech, sounds, history)    │
├──────────┬──────────────┬───────────────────────┤
│          │              │                       │
│  IChatClientProvider    │    IMessageStore       │
│  (default: resolves    │    (persistence)       │
│   IChatClient from DI) │                        │
│          │              │         │              │
│    IChatClient     ISpeechToText  │   ChatLookupAITool
│  (M.E.AI)          ITextToSpeech │   (optional AITool)
│                    (Shiny.Speech) │              │
│                         │         │              │
│                    IAudioPlayer   │              │
│                   (Shiny.Speech)  │              │
└─────────────────────────────────────────────────┘
```

## Dependencies

| Package | Purpose |
|---------|---------|
| [Microsoft.Extensions.AI](https://www.nuget.org/packages/Microsoft.Extensions.AI) | IChatClient abstraction |
| [Shiny.Speech](https://www.nuget.org/packages/Shiny.Speech) | Speech-to-text, text-to-speech, and audio playback |

## License

MIT
