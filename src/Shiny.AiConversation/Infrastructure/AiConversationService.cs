using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Shiny.Speech;

namespace Shiny.AiConversation.Infrastructure;

enum InterruptionKind { None, QuietWord, NewUtterance }
record InterruptionResult(InterruptionKind Kind, string? Text = null);

public class AiConversationService(
    IChatClientProvider chatClientProvider,
    ISpeechToTextService speechToText,
    ITextToSpeechService textToSpeech,
    IAudioPlayer audioPlayer,
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
    public Func<string, Task<Stream>>? SoundResolver { get; set; }
    public string? OkSound { get; set; }
    public string? CancelSound { get; set; }
    public string? ErrorSound { get; set; }
    public string? ThinkSound { get; set; }
    public string? RespondingSound { get; set; }
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
                        // AI asked a question — skip wake word, listen directly for the reply
                        this.lastResponseExpectedReply = false;
                        var reply = await speechToText.ListenUntilSilence(this.SpeechToTextOptions, ct);
                        if (!String.IsNullOrWhiteSpace(reply))
                            await this.TalkTo(reply, ct);
                    }
                    else
                    {
                        var keyword = await speechToText.ListenForKeyword([wakeWord], this.SpeechToTextOptions, ct);
                        if (keyword != null)
                        {
                            var utterance = await speechToText.ListenUntilSilence(this.SpeechToTextOptions, ct);
                            if (!String.IsNullOrWhiteSpace(utterance))
                                await this.TalkTo(utterance, ct);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected on stop
                await this.PlaySoundIf(this.CancelSound);
            }
            catch
            {
                await this.PlaySoundIf(this.ErrorSound);
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

        try
        {
            var continueListening = true;
            while (continueListening && !cancellationToken.IsCancellationRequested)
            {
                this.lastResponseExpectedReply = false;
                this.SetStatus(AiState.Listening);
                await this.PlaySoundIf(this.OkSound).ConfigureAwait(false);

                var utterance = await speechToText
                    .ListenUntilSilence(this.SpeechToTextOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (!String.IsNullOrWhiteSpace(utterance))
                    await this.TalkTo(utterance, cancellationToken).ConfigureAwait(false);

                continueListening = this.lastResponseExpectedReply && !String.IsNullOrWhiteSpace(utterance);
            }

            this.SetStatus(AiState.Idle);
        }
        catch (OperationCanceledException)
        {
            await this.PlaySoundIf(this.CancelSound).ConfigureAwait(false);
            this.SetStatus(AiState.Idle);
        }
        catch
        {
            await this.PlaySoundIf(this.ErrorSound).ConfigureAwait(false);
            this.SetStatus(AiState.Idle);
            throw;
        }
    }

    public async Task TalkTo(
        string message,
        CancellationToken cancellationToken
    )
    {
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
        await this.PlaySoundIf(this.ThinkSound).ConfigureAwait(false);
        var chatClient = await chatClientProvider
            .GetChatClient(cancellationToken)
            .ConfigureAwait(false);

        var chatMessages = this.CurrentChatMessages.ToList();
        var userMessage = new ChatMessage(ChatRole.User, message);
        
        var options = new ChatOptions();
        var context = contextProviders
            .Select(x => (x.GetSystemPrompts(this.Acknowledgement), x.GetTools()))
            .ToList();

        options.Tools = new List<AITool>();
        foreach (var cp in context)
        {
            var sysPrompts = cp.Item1.Select(x => new ChatMessage(ChatRole.System, x)).ToList();
            chatMessages.AddRange(sysPrompts);
            
            cp.Item2.ToList().ForEach(x => options.Tools.Add(x));
        }

        this.SetStatus(AiState.Responding);
        await this.PlaySoundIf(this.RespondingSound).ConfigureAwait(false);

        var wasReadAloud = this.Acknowledgement > AiAcknowledgement.AudioBlip;
        var response = await chatClient.GetResponseAsync(chatMessages, options, cancellationToken).ConfigureAwait(false);

        this.currentMessages.Add(userMessage);
        if (response.Text is { } responseText)
            this.currentMessages.Add(new ChatMessage(ChatRole.Assistant, responseText));

        var expectsResponse = response.Text?.TrimEnd().EndsWith('?') ?? false;
        this.lastResponseExpectedReply = expectsResponse;
        this.AiResponded?.Invoke(new AiResponse(response, wasReadAloud, expectsResponse));

        if (messageStore != null)
        {
            await messageStore
                .Store(userMessage.Text, response, cancellationToken)
                .ConfigureAwait(false);
        }

        if (wasReadAloud && response.Text is { } spokenText)
        {
            var interruption = await this.SpeakWithInterruptionSupport(spokenText, cancellationToken).ConfigureAwait(false);

            switch (interruption.Kind)
            {
                case InterruptionKind.QuietWord:
                    this.lastResponseExpectedReply = false;
                    return;

                case InterruptionKind.NewUtterance:
                    await this.ProcessMessage(interruption.Text!, cancellationToken).ConfigureAwait(false);
                    return;
            }
        }

        await this.PlaySoundIf(this.OkSound).ConfigureAwait(false);
    }
    

    async Task<InterruptionResult> SpeakWithInterruptionSupport(string text, CancellationToken cancellationToken)
    {
        var quietWords = this.QuietWords;
        if (quietWords == null || quietWords.Count == 0)
        {
            await textToSpeech.SpeakAsync(text, this.TextToSpeechOptions, cancellationToken).ConfigureAwait(false);
            return new InterruptionResult(InterruptionKind.None);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = linkedCts.Token;

        var speakTask = textToSpeech.SpeakAsync(text, this.TextToSpeechOptions, linkedToken);
        var listenTask = this.ListenDuringSpeech(quietWords, linkedToken);

        var completedTask = await Task.WhenAny(speakTask, listenTask).ConfigureAwait(false);

        if (completedTask == listenTask)
        {
            var result = await listenTask.ConfigureAwait(false);
            if (result.Kind != InterruptionKind.None)
            {
                await textToSpeech.StopAsync().ConfigureAwait(false);
                await linkedCts.CancelAsync().ConfigureAwait(false);
                return result;
            }
        }

        // TTS finished normally — cancel the listener
        await linkedCts.CancelAsync().ConfigureAwait(false);

        try { await listenTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }

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

        await foreach (var result in speechToText.ContinuousRecognize(this.SpeechToTextOptions, ct).ConfigureAwait(false))
        {
            if (String.IsNullOrWhiteSpace(result.Text))
                continue;

            var trimmed = result.Text.Trim();

            // Only treat as a quiet word if the entire utterance IS the quiet word
            if (quietOnlyPattern.IsMatch(trimmed))
                return new InterruptionResult(InterruptionKind.QuietWord, trimmed);

            // Any other speech — wait for final result before treating as new utterance
            if (result.IsFinal)
                return new InterruptionResult(InterruptionKind.NewUtterance, trimmed);
        }

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

    void SetStatus(AiState status)
    {
        this.Status = status;
        var handler = this.StatusChanged;
        if (handler != null)
            _ = Task.Run(() => handler.Invoke(status));
    }

    async Task PlaySoundIf(string? soundName)
    {
        if (String.IsNullOrEmpty(soundName) || this.SoundResolver == null)
            return;

        if (this.Acknowledgement == AiAcknowledgement.AudioBlip)
        {
            var stream = await this.SoundResolver(soundName);
            await audioPlayer.PlayAsync(stream);
        }
    }
}
