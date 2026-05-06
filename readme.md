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

builder.Services.AddShinyAiConversation(opts =>
{
    // Required — your IChatClientProvider implementation
    opts.SetChatClientProvider<MyChatClientProvider>();

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

### 2. Implement `IChatClientProvider`

This is how the library obtains a chat client. You control authentication, token management, and which AI backend to use.

```csharp
using Microsoft.Extensions.AI;
using Shiny.AiConversation;

public class MyChatClientProvider : IChatClientProvider
{
    public async Task<IChatClient> GetChatClient(CancellationToken cancelToken = default)
    {
        // Build and return any IChatClient — OpenAI, Azure, GitHub Copilot, etc.
        var client = new OpenAIClient("your-api-key");
        return client.GetChatClient("gpt-4o").AsIChatClient();
    }
}
```

### 3. Implement `IMessageStore` (optional)

Provide persistent storage for chat history. Without this, `GetChatHistory` and `ClearChatHistory` will throw.

```csharp
using Shiny.AiConversation;

public class MyMessageStore : IMessageStore
{
    public Task Store(AiChatMessage chatMessage, CancellationToken cancellationToken)
    {
        // Persist the message
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
| `StateChanged` | Event fired on any state change |
| `AiResponded` | Event fired with the AI's response text, timestamp, and whether it was read aloud |

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
│  (auth + AI backend)   │    (persistence)       │
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
