using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Shiny.Speech;

namespace Shiny.AiConversation.Infrastructure;

enum InterruptionKind { None, QuietWord, NewUtterance }
record InterruptionResult(InterruptionKind Kind, string? Text = null);

public class AiConversationService(
    IChatClientProvider chatClientProvider,
    ISpeechToTextService speechToText,
    ITextToSpeechService textToSpeech,
    ILogger<AiConversationService>? logger,
    ISoundProvider soundProvider,
    IEnumerable<IContextProvider> contextProviders,
    IMessageStore? messageStore = null
) : IAiConversationService
{
    readonly SemaphoreSlim semaphore = new(1, 1);
    CancellationTokenSource? wakeWordCts;
    bool lastResponseExpectedReply;

    public event Action<AiState>? StatusChanged;
    public event Action<AiResponse>? AiResponded;
    public bool IsWakeWordEnabled { get; private set; }
    public string? WakeWord { get; private set; }
    public bool InterruptionEnabled { get; set; }
    public IList<string>? QuietWords { get; set; } = ["cancel", "quiet", "shut up", "stop", "nevermind", "never mind", "hush"];
    public SpeechRecognitionOptions? SpeechToTextOptions { get; set; }
    public Shiny.Speech.TextToSpeechOptions? TextToSpeechOptions { get; set; }
    public AiState Status { get; private set; }
    public AiAcknowledgement Acknowledgement { get; set; } = AiAcknowledgement.Full;

    List<ChatMessage> currentMessages = [];
    public IReadOnlyList<ChatMessage> CurrentChatMessages => currentMessages.AsReadOnly();

    public void ClearCurrentChat()
    {
        this.currentMessages.Clear();
    }

    public async Task StartWakeWord(string wakeWord)
    {
        if (this.IsWakeWordEnabled)
            throw new InvalidOperationException("Wake word is already active.");

        // TODO: this can't be called if TalkTo is running
        await this.semaphore.WaitAsync();
        this.semaphore.Release();

        logger?.LogDebug("Starting wake word detection with word: {WakeWord}", wakeWord);
        this.WakeWord = wakeWord;
        this.IsWakeWordEnabled = true;
        this.wakeWordCts = new CancellationTokenSource();
        var ct = this.wakeWordCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    this.SetStatus(AiState.Listening);

                    if (this.lastResponseExpectedReply)
                    {
                        logger?.LogDebug("AI expects a reply, skipping wake word and listening directly");
                        this.lastResponseExpectedReply = false;
                        var reply = await speechToText.ListenUntilSilence(this.SpeechToTextOptions, ct);
                        logger?.LogDebug("Follow-up reply received: {Reply}", reply);
                        if (!String.IsNullOrWhiteSpace(reply))
                            await this.TalkTo(reply, ct);
                    }
                    else
                    {
                        logger?.LogDebug("Listening for wake word: {WakeWord}", wakeWord);
                        var keyword = await speechToText.ListenForKeyword([wakeWord], this.SpeechToTextOptions, ct);
                        if (keyword != null)
                        {
                            logger?.LogDebug("Wake word detected, listening for utterance");
                            var utterance = await speechToText.ListenUntilSilence(this.SpeechToTextOptions, ct);
                            logger?.LogDebug("Utterance received: {Utterance}", utterance);
                            if (!String.IsNullOrWhiteSpace(utterance))
                                await this.TalkTo(utterance, ct);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                logger?.LogDebug("Wake word loop cancelled");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Wake word loop error");
                await soundProvider.Play(AiAction.Error);
            }
            finally
            {
                this.IsWakeWordEnabled = false;
                this.WakeWord = null;
                this.SetStatus(AiState.Idle);
            }
        }, ct);
    }

    public void StopWakeWord()
    {
        logger?.LogDebug("Stopping wake word detection");
        if (this.wakeWordCts is { } cts)
        {
            cts.Cancel();
            cts.Dispose();
            this.wakeWordCts = null;
        }
    }

    public async Task ListenAndTalk(CancellationToken cancellationToken)
    {
        // TODO: this can't be called if TalkTo is running
        if (this.IsWakeWordEnabled)
            throw new InvalidOperationException("Cannot use ListenAndTalk while wake word is active.");

        logger?.LogDebug("ListenAndTalk started");
        try
        {
            var continueListening = true;
            while (continueListening && !cancellationToken.IsCancellationRequested)
            {
                this.lastResponseExpectedReply = false;
                this.SetStatus(AiState.Listening);
                await soundProvider.Play(AiAction.Ok).ConfigureAwait(false);

                logger?.LogDebug("Listening for speech input");
                var utterance = await speechToText
                    .ListenUntilSilence(this.SpeechToTextOptions, cancellationToken)
                    .ConfigureAwait(false);

                logger?.LogDebug("Speech input received: {Utterance}", utterance);
                if (!String.IsNullOrWhiteSpace(utterance))
                    await this.TalkTo(utterance, cancellationToken).ConfigureAwait(false);

                continueListening = this.lastResponseExpectedReply && !String.IsNullOrWhiteSpace(utterance);
                logger?.LogDebug("Continue listening: {ContinueListening}, ExpectsReply: {ExpectsReply}", continueListening, this.lastResponseExpectedReply);
            }

            this.SetStatus(AiState.Idle);
        }
        catch (OperationCanceledException)
        {
            logger?.LogDebug("ListenAndTalk cancelled");
            await soundProvider.Play(AiAction.Cancel).ConfigureAwait(false);
            this.SetStatus(AiState.Idle);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "ListenAndTalk error");
            await soundProvider.Play(AiAction.Error).ConfigureAwait(false);
            this.SetStatus(AiState.Idle);
            throw;
        }
    }

    public async Task TalkTo(
        string message,
        CancellationToken cancellationToken
    )
    {
        logger?.LogDebug("TalkTo called with message: {Message}", message);
        await this.semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await this.ProcessMessage(message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this.SetStatus(AiState.Idle);
            this.semaphore.Release();
        }
    }

    async Task ProcessMessage(string message, CancellationToken cancellationToken)
    {
        this.SetStatus(AiState.Thinking);
        await soundProvider.Play(AiAction.Think).ConfigureAwait(false);
        
        logger?.LogDebug("Getting chat client");
        var chatClient = await chatClientProvider
            .GetChatClient(cancellationToken)
            .ConfigureAwait(false);

        var chatMessages = this.CurrentChatMessages.ToList();
        var userMessage = new ChatMessage(ChatRole.User, message);
        chatMessages.Add(userMessage);

        var aiContext = new AiContext { Acknowledgement = this.Acknowledgement };
        foreach (var cp in contextProviders)
            await cp.Apply(aiContext).ConfigureAwait(false);

        chatMessages.AddRange(aiContext.SystemPrompts.Select(x => new ChatMessage(ChatRole.System, x)));

        var options = new ChatOptions();
        options.Tools = aiContext.Tools;

        logger?.LogDebug(
            "Sending {MessageCount} messages to chat client with {ToolCount} tools, Acknowledgement: {Acknowledgement}",
            chatMessages.Count,
            options.Tools.Count,
            this.Acknowledgement
        );

        this.SetStatus(AiState.Responding);
        await soundProvider.Play(AiAction.Respond).ConfigureAwait(false);

        var wasReadAloud = this.Acknowledgement > AiAcknowledgement.AudioBlip;
        var response = await chatClient.GetResponseAsync(chatMessages, options, cancellationToken).ConfigureAwait(false);

        logger?.LogDebug(
            "AI response received - HasText: {HasText}, TextLength: {TextLength}, WasReadAloud: {WasReadAloud}",
            response.Text != null,
            response.Text?.Length ?? 0,
            wasReadAloud
        );

        this.currentMessages.Add(userMessage);
        if (response.Text is { } responseText)
            this.currentMessages.Add(new ChatMessage(ChatRole.Assistant, responseText));

        var expectsResponse = response.Text?.TrimEnd().EndsWith('?') ?? false;
        this.lastResponseExpectedReply = expectsResponse;
        logger?.LogDebug("ExpectsResponse: {ExpectsResponse}", expectsResponse);
        this.AiResponded?.Invoke(new AiResponse(response, wasReadAloud, expectsResponse));

        if (messageStore != null)
        {
            await messageStore
                .Store(userMessage.Text, response, cancellationToken)
                .ConfigureAwait(false);
        }

        if (wasReadAloud && response.Text is { } spokenText)
        {
            logger?.LogDebug("Starting TTS for response ({Length} chars), InterruptionEnabled: {InterruptionEnabled}", spokenText.Length, this.InterruptionEnabled);
            var interruption = await this.SpeakWithInterruptionSupport(spokenText, cancellationToken).ConfigureAwait(false);

            switch (interruption.Kind)
            {
                case InterruptionKind.QuietWord:
                    logger?.LogDebug("TTS interrupted by quiet word: {Word}", interruption.Text);
                    this.lastResponseExpectedReply = false;
                    return;

                case InterruptionKind.NewUtterance:
                    logger?.LogDebug("TTS interrupted by new utterance: {Utterance}", interruption.Text);
                    await this.ProcessMessage(interruption.Text!, cancellationToken).ConfigureAwait(false);
                    return;
            }
        }
        else
        {
            logger?.LogDebug("Skipping TTS - WasReadAloud: {WasReadAloud}, HasText: {HasText}", wasReadAloud, response.Text != null);
        }

        await soundProvider.Play(AiAction.Ok).ConfigureAwait(false);
    }


    async Task<InterruptionResult> SpeakWithInterruptionSupport(string text, CancellationToken cancellationToken)
    {
        var quietWords = this.QuietWords;
        if (!this.InterruptionEnabled || quietWords == null || quietWords.Count == 0)
        {
            logger?.LogDebug("Speaking without interruption support (InterruptionEnabled: {Enabled}, QuietWords: {Count})", this.InterruptionEnabled, quietWords?.Count ?? 0);
            await textToSpeech.SpeakAsync(text, this.TextToSpeechOptions, cancellationToken).ConfigureAwait(false);
            logger?.LogDebug("TTS completed (no interruption path)");
            return new InterruptionResult(InterruptionKind.None);
        }

        logger?.LogDebug("Speaking with interruption support, {Count} quiet words configured", quietWords.Count);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = linkedCts.Token;

        var speakTask = textToSpeech.SpeakAsync(text, this.TextToSpeechOptions, linkedToken);
        var listenTask = this.ListenDuringSpeech(quietWords, linkedToken);

        var completedTask = await Task.WhenAny(speakTask, listenTask).ConfigureAwait(false);
        logger?.LogDebug("WhenAny completed - SpeakFinishedFirst: {SpeakFirst}", completedTask == speakTask);

        if (completedTask == listenTask)
        {
            var result = await listenTask.ConfigureAwait(false);
            logger?.LogDebug("Listen task completed first with result: {Kind}, Text: {Text}", result.Kind, result.Text);

            if (result.Kind != InterruptionKind.None)
            {
                logger?.LogDebug("Stopping TTS due to interruption");
                await textToSpeech.StopAsync().ConfigureAwait(false);
                await linkedCts.CancelAsync().ConfigureAwait(false);
                return result;
            }

            logger?.LogDebug("Listener ended with no interruption, waiting for TTS to finish");
            await speakTask.ConfigureAwait(false);
        }

        logger?.LogDebug("TTS finished, cancelling listener");
        await linkedCts.CancelAsync().ConfigureAwait(false);

        try { await listenTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }

        logger?.LogDebug("SpeakWithInterruptionSupport completed normally");
        return new InterruptionResult(InterruptionKind.None);
    }

    async Task<InterruptionResult> ListenDuringSpeech(IList<string> quietWords, CancellationToken ct)
    {
        // Match text that is ONLY a quiet word/phrase (with optional surrounding whitespace/punctuation)
        // e.g., "stop" or "shut up" but NOT "cancel this appointment"
        var quietOnlyPattern = new Regex(
            @"^\s*(" + String.Join("|", quietWords.Select(Regex.Escape)) + @")\s*[.!]?\s*$",
            RegexOptions.IgnoreCase
        );

        logger?.LogDebug("ListenDuringSpeech started, monitoring for interruptions");
        await foreach (var result in speechToText.ContinuousRecognize(this.SpeechToTextOptions, ct).ConfigureAwait(false))
        {
            if (String.IsNullOrWhiteSpace(result.Text))
                continue;

            var trimmed = result.Text.Trim();
            logger?.LogDebug("Speech detected during TTS - Text: {Text}, IsFinal: {IsFinal}", trimmed, result.IsFinal);

            // Only treat as a quiet word if the entire utterance IS the quiet word
            if (quietOnlyPattern.IsMatch(trimmed))
            {
                logger?.LogDebug("Quiet word matched: {Word}", trimmed);
                return new InterruptionResult(InterruptionKind.QuietWord, trimmed);
            }

            // Any other speech — wait for final result before treating as new utterance
            if (result.IsFinal)
            {
                logger?.LogDebug("New utterance detected: {Utterance}", trimmed);
                return new InterruptionResult(InterruptionKind.NewUtterance, trimmed);
            }
        }

        logger?.LogDebug("ContinuousRecognize stream ended with no interruption");
        return new InterruptionResult(InterruptionKind.None);
    }

    public Task<IReadOnlyList<AiChatMessage>> GetChatHistory(
        string? messageContains = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        int? limit = null
    )
    {
        if (messageStore == null)
            throw new InvalidOperationException("MessageStore is not set");

        return messageStore.Query(messageContains, startDate, endDate, limit);
    }

    public Task ClearChatHistory(DateTimeOffset? beforeDate = null, CancellationToken cancellationToken = default)
    {
        if (messageStore == null)
            throw new InvalidOperationException("MessageStore is not set");

        return messageStore.Clear(beforeDate);
    }

    public async Task<AccessState> RequestAccess()
    {
        var access = await speechToText.RequestAccess().ConfigureAwait(false);
        return access == AccessState.Available
            ? AccessState.Available
            : AccessState.Restricted;
    }

    void SetStatus(AiState status)
    {
        logger?.LogDebug("Status changing: {OldStatus} -> {NewStatus}", this.Status, status);
        this.Status = status;
        this.StatusChanged?.Invoke(status);
    }
}
