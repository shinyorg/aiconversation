using Microsoft.Extensions.AI;

namespace Shiny.AiConversation;

/// <summary>
/// A centralized AI service that manages chat interactions, speech recognition,
/// wake word detection, and text-to-speech responses.
/// </summary>
public interface IAiConversationService
{
    /// <summary>
    /// Raised when any observable state changes (Status, CurrentToken, IsWakeWordEnabled, etc).
    /// </summary>
    event Action<AiState> StatusChanged;

    /// <summary>
    /// Raised when the AI produces a response. The string parameter contains the full response text.
    /// </summary>
    event Action<AiResponse>? AiResponded;

    /// <summary>
    /// The currently active wake word, or null if wake word detection is not running.
    /// </summary>
    string? WakeWord { get; }

    /// <summary>
    /// Begins listening for the specified wake word. Waits for any in-progress AI tasks to complete before starting.
    /// Once the wake word is detected, captures the user's utterance via speech-to-text and sends it through TalkTo.
    /// Loops continuously until <see cref="StopWakeWord"/> is called.
    /// </summary>
    /// <param name="wakeWord">The keyword phrase to listen for (e.g. "Hey Assistant").</param>
    /// <exception cref="InvalidOperationException">Thrown if wake word detection is already active.</exception>
    Task StartWakeWord(string wakeWord);

    /// <summary>
    /// Stops the active wake word detection loop and returns the service to an idle state.
    /// </summary>
    void StopWakeWord();

    /// <summary>
    /// Callback that resolves a sound file name to a playable stream.
    /// Must be set for sound effects to work. In MAUI apps, use:
    /// <c>aiService.SoundResolver = name => FileSystem.OpenAppPackageFileAsync(name);</c>
    /// </summary>
    Func<string, Task<Stream>>? SoundResolver { get; set; }

    /// <summary>
    /// Sound file name played when the service acknowledges a successful interaction.
    /// Only played when <see cref="Acknowledgement"/> is <see cref="AiAcknowledgement.AudioBlip"/> and <see cref="SoundResolver"/> is set.
    /// </summary>
    string? OkSound { get; set; }

    /// <summary>
    /// Sound file name played when an operation is cancelled.
    /// Only played when <see cref="Acknowledgement"/> is <see cref="AiAcknowledgement.AudioBlip"/> and <see cref="SoundResolver"/> is set.
    /// </summary>
    string? CancelSound { get; set; }

    /// <summary>
    /// Sound file name played when an error occurs.
    /// Only played when <see cref="Acknowledgement"/> is <see cref="AiAcknowledgement.AudioBlip"/> and <see cref="SoundResolver"/> is set.
    /// </summary>
    string? ErrorSound { get; set; }

    /// <summary>
    /// Sound file name played when the AI begins processing a request.
    /// Only played when <see cref="Acknowledgement"/> is <see cref="AiAcknowledgement.AudioBlip"/> and <see cref="SoundResolver"/> is set.
    /// </summary>
    string? ThinkSound { get; set; }

    /// <summary>
    /// Sound file name played when the AI begins streaming its response.
    /// Only played when <see cref="Acknowledgement"/> is <see cref="AiAcknowledgement.AudioBlip"/> and <see cref="SoundResolver"/> is set.
    /// </summary>
    string? RespondingSound { get; set; }
    
    /// <summary>
    /// The current processing state of the service.
    /// </summary>
    AiState Status { get; }

    /// <summary>
    /// Controls how the AI acknowledges responses. Determines whether sounds are played,
    /// text-to-speech is used, or responses are kept concise.
    /// </summary>
    AiAcknowledgement Acknowledgement { get; set; }

    /// <summary>
    /// System prompts prepended to every chat request. A time-based system prompt is always included automatically.
    /// </summary>
    IList<string> SystemPrompts { get; set; }

    /// <summary>
    /// The in-memory chat messages for the current conversation session.
    /// </summary>
    IReadOnlyList<ChatMessage> CurrentChatMessages { get; }

    /// <summary>
    /// Clears all in-memory chat messages for the current conversation session.
    /// Does not affect persisted history.
    /// </summary>
    void ClearCurrentChat();

    /// <summary>
    /// Activates speech-to-text to collect a single utterance and sends it to the AI.
    /// This is intended for manual "push to talk" scenarios when wake word is not in use.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the listening and/or AI processing.</param>
    /// <exception cref="InvalidOperationException">Thrown if wake word detection is currently active.</exception>
    Task ListenAndTalk(CancellationToken cancellationToken);

    /// <summary>
    /// Sends a text message directly to the AI for processing.
    /// </summary>
    /// <param name="message">The user message to send.</param>
    /// <param name="cancellationToken">Token to cancel the AI processing.</param>
    Task TalkTo(string message, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves persisted chat history from the document store with optional filtering.
    /// </summary>
    /// <param name="messageContains">Optional text filter to match against message content.</param>
    /// <param name="startDate">Optional inclusive start date to filter messages.</param>
    /// <param name="endDate">Optional inclusive end date to filter messages.</param>
    /// <param name="limit">Optional maximum number of messages to return.</param>
    /// <returns>A list of chat messages ordered by timestamp.</returns>
    Task<IReadOnlyList<AiChatMessage>> GetChatHistory(
        string? messageContains = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        int? limit = null
    );
    
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="beforeDate"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task ClearChatHistory(DateTimeOffset? beforeDate = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the current processing state of the AI service.
/// </summary>
public enum AiState
{
    /// <summary>The service is idle and ready for input.</summary>
    Idle,
    /// <summary>The service is actively listening for speech input.</summary>
    Listening,
    /// <summary>The service is waiting for the AI to process a request.</summary>
    Thinking,
    /// <summary>The AI is streaming its response.</summary>
    Responding
}

/// <summary>
/// Controls how the AI service acknowledges and delivers responses.
/// </summary>
public enum AiAcknowledgement
{
    /// <summary>No audio feedback or text-to-speech.</summary>
    None,
    /// <summary>Short audio cues are played at state transitions.</summary>
    AudioBlip,
    /// <summary>Text-to-speech is used with a concise system prompt.</summary>
    LessWordy,
    /// <summary>Text-to-speech is used with full, unmodified responses.</summary>
    Full
}

public record AiResponse(
    ChatResponse Response,
    bool WasReadAloud
);