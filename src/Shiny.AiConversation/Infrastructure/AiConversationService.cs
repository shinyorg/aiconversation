using System.Text;
using Microsoft.Extensions.AI;
using Shiny.Speech;

namespace Shiny.AiConversation.Infrastructure;

public class AiConversationService(
    IChatClientProvider chatClientProvider,
    ISpeechToTextService speechToText,
    ITextToSpeechService textToSpeech,
    IAudioPlayer audioPlayer,
    TimeProvider timeProvider,
    IEnumerable<AITool> tools,
    IMessageStore? messageStore = null // optional
) : IAiConversationService
{
    readonly SemaphoreSlim semaphore = new(1, 1);
    CancellationTokenSource? wakeWordCts;

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
    public AiState Status { get; private set; }
    public AiAcknowledgement Acknowledgement { get; set; } = AiAcknowledgement.Full;
    public IList<string> SystemPrompts { get; set; } = [];

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
                    var keyword = await speechToText.ListenForKeyword([wakeWord], cancellationToken: ct);
                    if (keyword != null)
                    {
                        var utterance = await speechToText.ListenUntilSilence(cancellationToken: ct);
                        if (!String.IsNullOrWhiteSpace(utterance))
                            await this.TalkTo(utterance, ct);
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

        this.SetStatus(AiState.Listening);
        try
        {
            await this.PlaySoundIf(this.OkSound);

            var utterance = await speechToText.ListenUntilSilence(cancellationToken: cancellationToken);
            if (!String.IsNullOrWhiteSpace(utterance))
                await this.TalkTo(utterance, cancellationToken);
            else
                this.SetStatus(AiState.Idle);
        }
        catch (OperationCanceledException)
        {
            await this.PlaySoundIf(this.CancelSound);
            this.SetStatus(AiState.Idle);
        }
        catch
        {
            await this.PlaySoundIf(this.ErrorSound);
            this.SetStatus(AiState.Idle);
            throw;
        }
    }

    public async Task TalkTo(
        string message,
        CancellationToken cancellationToken
    )
    {
        await this.semaphore.WaitAsync(cancellationToken);
        try
        {
            this.SetStatus(AiState.Thinking);
            await this.PlaySoundIf(this.ThinkSound);
            var chatClient = await chatClientProvider
                .GetChatClient(cancellationToken)
                .ConfigureAwait(false);

            var chatMessages = this.BuildMessages(message);

            var userMessage = new ChatMessage(ChatRole.User, message);
            this.currentMessages.Add(userMessage);

            if (messageStore != null)
            {
                await messageStore.Store(
                    userMessage,
                    cancellationToken
                );
            }

            var options = new ChatOptions();
            var toolList = tools.ToList();
            if (toolList.Count > 0)
                options.Tools = toolList;

            this.SetStatus(AiState.Responding);
            await this.PlaySoundIf(this.RespondingSound);

            var wasReadAloud = this.Acknowledgement > AiAcknowledgement.AudioBlip;

            await foreach (var update in chatClient.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
            {
                if (update.Text is { } text)
                {
                    this.currentMessages.Add(new ChatMessage(ChatRole.Assistant, text));
                    var usage = update.Contents.OfType<UsageContent>().FirstOrDefault()?.Details;

                    this.AiResponded?.Invoke(new AiResponse(
                        update, 
                        usage, 
                        update.FinishReason != null, 
                        wasReadAloud
                    ));
    
                    if (messageStore != null)
                    {
                        await messageStore.Store(
                            userMessage.Text,
                            update,
                            usage,
                            cancellationToken
                        );
                    }
                    if (wasReadAloud)
                        await textToSpeech.SpeakAsync(text, cancellationToken: cancellationToken);
                }
            }

            await this.PlaySoundIf(this.OkSound);
        }
        catch (OperationCanceledException)
        {
            await this.PlaySoundIf(this.CancelSound);
        }
        catch
        {
            await this.PlaySoundIf(this.ErrorSound);
            throw;
        }
        finally
        {
            this.SetStatus(AiState.Idle);
            this.semaphore.Release();
        }
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

    List<ChatMessage> BuildMessages(string userMessage)
    {
        var messages = new List<ChatMessage>();

        // system prompts
        var timePrompt = new ChatMessage(
            ChatRole.System,
            $"The current time is {timeProvider.GetUtcNow().ToLocalTime():hh:mm tt} on {timeProvider.GetUtcNow().ToLocalTime():MMMM dd, yyyy}."
        );
        messages.Add(timePrompt);

        foreach (var prompt in this.SystemPrompts)
            messages.Add(new ChatMessage(ChatRole.System, prompt));

        if (this.Acknowledgement == AiAcknowledgement.LessWordy)
            messages.Add(new ChatMessage(ChatRole.System, "Be concise and brief in your responses. Avoid unnecessary elaboration."));

        foreach (var message in this.CurrentChatMessages)
            messages.Add(message);

        // current user message
        messages.Add(new ChatMessage(ChatRole.User, userMessage));
        return messages;
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
