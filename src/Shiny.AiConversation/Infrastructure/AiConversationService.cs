using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Shiny.Speech;

namespace Shiny.AiConversation.Infrastructure;

enum InterruptionKind { None, QuietWord, NewUtterance }
record InterruptionResult(InterruptionKind Kind, string? Text = null, float? Confidence = null);

public class AiConversationService(
    IChatClientProvider chatClientProvider,
    ISpeechToTextService speechToText,
    ITextToSpeechService textToSpeech,
    ILogger<AiConversationService>? logger,
    ISoundProvider soundProvider,
    IEnumerable<IContextProvider> contextProviders,
    IMessageStore? messageStore = null
) : IAiConversationService, IAsyncDisposable
{
    readonly SemaphoreSlim semaphore = new(1, 1);
    readonly object sessionLock = new();
    SessionMode sessionMode = SessionMode.None;
    CancellationTokenSource? sessionCts;
    Channel<SpeechRecognitionResult>? recognitionChannel;
    Channel<string>? keywordChannel;
    bool sessionStarted;
    bool lastResponseExpectedReply;

    public event Action<AiState>? StatusChanged;
    public event Action<AiResponse>? AiResponded;
    public event Action<SpeechRecognitionResult>? SpeechResultReceived;
    public bool IsWakeWordEnabled => this.sessionMode == SessionMode.WakeWord;
    public string? WakeWord { get; private set; }
    public bool InterruptionEnabled { get; set; }
    public IList<string>? QuietWords { get; set; } = ["cancel", "quiet", "shut up", "stop", "nevermind", "never mind", "hush"];
    public SpeechRecognitionOptions? SpeechToTextOptions { get; set; }
    public Shiny.Speech.TextToSpeechOptions? TextToSpeechOptions { get; set; }
    public AiState Status { get; private set; }
    public AiAcknowledgement Acknowledgement { get; set; } = AiAcknowledgement.Full;
    public float InterruptionMinConfidence { get; set; } = 0.5f;

    List<ChatMessage> currentMessages = [];
    public IReadOnlyList<ChatMessage> CurrentChatMessages => currentMessages.AsReadOnly();

    public void ClearCurrentChat()
    {
        this.currentMessages.Clear();
    }

    public async Task StartWakeWord(string wakeWord)
    {
        if (this.sessionMode != SessionMode.None)
            throw new InvalidOperationException("A conversation session is already active.");

        logger?.LogDebug("Starting wake word detection with word: {WakeWord}", wakeWord);
        this.WakeWord = wakeWord;
        await this.StartSessionAsync(SessionMode.WakeWord, [wakeWord]).ConfigureAwait(false);
        var ct = this.sessionCts!.Token;

        _ = Task.Run(() => this.RunWakeWordLoop(wakeWord, ct), ct);
    }

    public Task StopWakeWord()
    {
        logger?.LogDebug("Stopping wake word detection");
        if (this.sessionMode != SessionMode.WakeWord)
            return Task.CompletedTask;

        return this.EndSessionAsync();
    }

    public async Task ListenAndTalk(CancellationToken cancellationToken)
    {
        if (this.sessionMode == SessionMode.WakeWord)
            throw new InvalidOperationException("Cannot use ListenAndTalk while wake word is active.");

        if (this.sessionMode != SessionMode.None)
            throw new InvalidOperationException("A conversation session is already active.");

        logger?.LogDebug("ListenAndTalk started");
        await this.StartSessionAsync(SessionMode.PushToTalk, null).ConfigureAwait(false);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.sessionCts!.Token);
        var ct = linkedCts.Token;

        try
        {
            var continueListening = true;
            while (continueListening && !ct.IsCancellationRequested)
            {
                this.lastResponseExpectedReply = false;
                this.SetStatus(AiState.Listening);
                await soundProvider.Play(AiAction.Ok).ConfigureAwait(false);

                logger?.LogDebug("Listening for utterance");
                var utterance = await this.ReadNextUtteranceAsync(ct).ConfigureAwait(false);
                logger?.LogDebug("Utterance received: {Utterance}", utterance);

                if (!String.IsNullOrWhiteSpace(utterance))
                    await this.TalkTo(utterance, ct).ConfigureAwait(false);

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
        finally
        {
            await this.EndSessionAsync().ConfigureAwait(false);
        }
    }

    public async Task TalkTo(string message, CancellationToken cancellationToken)
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

    async Task RunWakeWordLoop(string wakeWord, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                this.SetStatus(AiState.Listening);

                string? utterance;
                if (this.lastResponseExpectedReply)
                {
                    logger?.LogDebug("AI expects a reply, capturing follow-up utterance directly");
                    this.lastResponseExpectedReply = false;
                    utterance = await this.ReadNextUtteranceAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    logger?.LogDebug("Waiting for wake word: {WakeWord}", wakeWord);
                    await this.WaitForKeywordAsync(ct).ConfigureAwait(false);
                    logger?.LogDebug("Wake word detected, capturing utterance");
                    utterance = await this.ReadNextUtteranceAsync(ct).ConfigureAwait(false);
                }

                logger?.LogDebug("Utterance received: {Utterance}", utterance);
                if (!String.IsNullOrWhiteSpace(utterance))
                    await this.TalkTo(utterance!, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            logger?.LogDebug("Wake word loop cancelled");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Wake word loop error");
            await soundProvider.Play(AiAction.Error).ConfigureAwait(false);
        }
        finally
        {
            this.SetStatus(AiState.Idle);
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
        if (!this.InterruptionEnabled || quietWords == null || quietWords.Count == 0 || !this.sessionStarted)
        {
            logger?.LogDebug(
                "Speaking without interruption support (InterruptionEnabled: {Enabled}, QuietWords: {Count}, SessionActive: {Active})",
                this.InterruptionEnabled,
                quietWords?.Count ?? 0,
                this.sessionStarted
            );
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
        // e.g. "stop" or "shut up" but NOT "cancel this appointment"
        var quietOnlyPattern = new Regex(
            @"^\s*(" + String.Join("|", quietWords.Select(Regex.Escape)) + @")\s*[.!]?\s*$",
            RegexOptions.IgnoreCase
        );

        logger?.LogDebug("ListenDuringSpeech started, monitoring recognition stream");

        // Drain anything that arrived before TTS started to avoid stale results.
        this.DrainRecognitionChannel();

        var channel = this.recognitionChannel
            ?? throw new InvalidOperationException("Cannot listen for interruptions without an active speech session.");

        await foreach (var result in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (String.IsNullOrWhiteSpace(result.Text))
                continue;

            // Filter results captured while TTS is actively speaking unless confidence is high enough to
            // overcome likely echo from the synthesized voice.
            if (textToSpeech.IsSpeaking && result.Confidence is { } conf && conf < this.InterruptionMinConfidence)
            {
                logger?.LogDebug(
                    "Ignoring low-confidence result during TTS - Text: {Text}, Confidence: {Confidence}",
                    result.Text,
                    conf
                );
                continue;
            }

            var trimmed = result.Text.Trim();
            logger?.LogDebug(
                "Speech detected during TTS - Text: {Text}, IsFinal: {IsFinal}, Confidence: {Confidence}",
                trimmed,
                result.IsFinal,
                result.Confidence
            );

            if (quietOnlyPattern.IsMatch(trimmed))
            {
                logger?.LogDebug("Quiet word matched: {Word}", trimmed);
                return new InterruptionResult(InterruptionKind.QuietWord, trimmed, result.Confidence);
            }

            if (result.IsFinal)
            {
                logger?.LogDebug("New utterance detected: {Utterance}", trimmed);
                return new InterruptionResult(InterruptionKind.NewUtterance, trimmed, result.Confidence);
            }
        }

        logger?.LogDebug("Recognition channel completed with no interruption");
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

    public async ValueTask DisposeAsync()
    {
        await this.EndSessionAsync().ConfigureAwait(false);
        this.semaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    void SetStatus(AiState status)
    {
        logger?.LogDebug("Status changing: {OldStatus} -> {NewStatus}", this.Status, status);
        this.Status = status;
        this.StatusChanged?.Invoke(status);
    }

    enum SessionMode { None, WakeWord, PushToTalk }

    async Task StartSessionAsync(SessionMode mode, string[]? keywords)
    {
        Channel<SpeechRecognitionResult> resultChannel;
        Channel<string> kwChannel;

        lock (this.sessionLock)
        {
            if (this.sessionMode != SessionMode.None)
                throw new InvalidOperationException("A conversation session is already active.");

            this.sessionMode = mode;
            this.sessionCts = new CancellationTokenSource();
            resultChannel = Channel.CreateUnbounded<SpeechRecognitionResult>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
            kwChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
            this.recognitionChannel = resultChannel;
            this.keywordChannel = kwChannel;
        }

        speechToText.ResultReceived += this.OnSpeechResult;
        speechToText.KeywordHeard += this.OnKeywordHeard;
        speechToText.Error += this.OnSpeechError;

        var options = this.SpeechToTextOptions ?? new SpeechRecognitionOptions();
        if (keywords is { Length: > 0 })
            options = options with { Keywords = keywords };

        try
        {
            await speechToText.Start(options).ConfigureAwait(false);
            this.sessionStarted = true;
            logger?.LogDebug("Speech session started (mode: {Mode}, keywords: {Keywords})", mode, keywords?.Length ?? 0);
        }
        catch
        {
            speechToText.ResultReceived -= this.OnSpeechResult;
            speechToText.KeywordHeard -= this.OnKeywordHeard;
            speechToText.Error -= this.OnSpeechError;
            lock (this.sessionLock)
            {
                this.sessionMode = SessionMode.None;
                this.sessionCts?.Dispose();
                this.sessionCts = null;
                this.recognitionChannel = null;
                this.keywordChannel = null;
            }
            throw;
        }
    }

    async Task EndSessionAsync()
    {
        CancellationTokenSource? cts;
        Channel<SpeechRecognitionResult>? resultChannel;
        Channel<string>? kwChannel;

        lock (this.sessionLock)
        {
            if (this.sessionMode == SessionMode.None)
                return;

            cts = this.sessionCts;
            resultChannel = this.recognitionChannel;
            kwChannel = this.keywordChannel;

            this.sessionMode = SessionMode.None;
            this.sessionCts = null;
            this.recognitionChannel = null;
            this.keywordChannel = null;
            this.WakeWord = null;
        }

        speechToText.ResultReceived -= this.OnSpeechResult;
        speechToText.KeywordHeard -= this.OnKeywordHeard;
        speechToText.Error -= this.OnSpeechError;

        resultChannel?.Writer.TryComplete();
        kwChannel?.Writer.TryComplete();

        try
        {
            if (cts is not null)
            {
                await cts.CancelAsync().ConfigureAwait(false);
                cts.Dispose();
            }
        }
        catch (ObjectDisposedException) { }

        try
        {
            if (this.sessionStarted)
                await speechToText.Stop().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Error stopping speech-to-text service");
        }
        finally
        {
            this.sessionStarted = false;
            this.SetStatus(AiState.Idle);
        }
    }

    void OnSpeechResult(object? sender, SpeechRecognitionResult result)
    {
        this.SpeechResultReceived?.Invoke(result);
        this.recognitionChannel?.Writer.TryWrite(result);
    }

    void OnKeywordHeard(object? sender, string keyword)
    {
        this.keywordChannel?.Writer.TryWrite(keyword);
    }

    void OnSpeechError(object? sender, SpeechRecognitionError error)
    {
        logger?.LogError(error.Exception, "Speech recognition error: {Message}", error.Message);
        this.recognitionChannel?.Writer.TryComplete(error.Exception);
        this.keywordChannel?.Writer.TryComplete(error.Exception);
    }

    async Task WaitForKeywordAsync(CancellationToken ct)
    {
        var channel = this.keywordChannel
            ?? throw new InvalidOperationException("Speech session is not active.");

        // Flush stale keywords that may have arrived before the caller started waiting.
        while (channel.Reader.TryRead(out _)) { }

        await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false);
        channel.Reader.TryRead(out _);
    }

    async Task<string?> ReadNextUtteranceAsync(CancellationToken ct)
    {
        var channel = this.recognitionChannel
            ?? throw new InvalidOperationException("Speech session is not active.");

        // Flush partial/non-final results that arrived before this turn started so the next final
        // result truly represents the current user utterance.
        this.DrainRecognitionChannel();

        await foreach (var result in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (result.IsFinal)
            {
                var text = result.Text?.Trim();
                if (!String.IsNullOrWhiteSpace(text))
                    return text;
            }
        }

        return null;
    }

    void DrainRecognitionChannel()
    {
        if (this.recognitionChannel is { } channel)
        {
            while (channel.Reader.TryRead(out _)) { }
        }
    }
}
