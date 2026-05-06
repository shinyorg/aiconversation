using Imposter.Abstractions;
using Microsoft.Extensions.AI;
using Shiny.Maui.AiConversation.Infrastructure;
using Shiny.Speech;

namespace Shiny.Maui.AiConversation.Tests;

public class AiConversationServiceTests
{
    static readonly DateTimeOffset FixedTime = new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    static (AiConversationService Service,
        IChatClientProviderImposter ChatClientProvider,
        IChatClientImposter ChatClient,
        ISpeechToTextServiceImposter SpeechToText,
        ITextToSpeechServiceImposter TextToSpeech,
        IAudioPlayerImposter AudioPlayer,
        IMessageStoreImposter MessageStore,
        FakeTimeProvider TimeProvider) CreateService(bool withMessageStore = true)
    {
        var chatClientProvider = IChatClientProvider.Imposter();
        var chatClient = IChatClient.Imposter();
        var speechToText = ISpeechToTextService.Imposter();
        var textToSpeech = ITextToSpeechService.Imposter();
        var audioPlayer = IAudioPlayer.Imposter();
        var messageStore = IMessageStore.Imposter();
        var timeProvider = new FakeTimeProvider(FixedTime);

        chatClientProvider
            .GetChatClient(Arg<CancellationToken>.Any())
            .ReturnsAsync(chatClient.Instance());

        messageStore
            .Store(Arg<AiChatMessage>.Any(), Arg<CancellationToken>.Any())
            .Returns(Task.CompletedTask);

        textToSpeech
            .SpeakAsync(Arg<string>.Any(), Arg<Shiny.Speech.TextToSpeechOptions?>.Any(), Arg<CancellationToken>.Any())
            .Returns(Task.CompletedTask);

        var service = new AiConversationService(
            chatClientProvider.Instance(),
            speechToText.Instance(),
            textToSpeech.Instance(),
            audioPlayer.Instance(),
            timeProvider,
            [],
            withMessageStore ? messageStore.Instance() : null
        );

        return (service, chatClientProvider, chatClient, speechToText, textToSpeech, audioPlayer, messageStore, timeProvider);
    }

    #region TalkTo

    [Test]
    public async Task TalkTo_AddsUserMessageToCurrentChat()
    {
        var (service, _, chatClient, _, _, _, _, _) = CreateService(withMessageStore: false);
        SetupStreamingResponse(chatClient, "Hello back!");

        await service.TalkTo("Hello", CancellationToken.None);

        await Assert.That(service.CurrentChatMessages.Count).IsEqualTo(2);
        await Assert.That(service.CurrentChatMessages[0].Role).IsEqualTo(ChatRole.User);
        await Assert.That(service.CurrentChatMessages[0].Text).IsEqualTo("Hello");
    }

    [Test]
    public async Task TalkTo_AddsAssistantResponseToCurrentChat()
    {
        var (service, _, chatClient, _, _, _, _, _) = CreateService(withMessageStore: false);
        SetupStreamingResponse(chatClient, "I am AI");

        await service.TalkTo("Hi", CancellationToken.None);

        await Assert.That(service.CurrentChatMessages.Count).IsEqualTo(2);
        await Assert.That(service.CurrentChatMessages[1].Role).IsEqualTo(ChatRole.Assistant);
        await Assert.That(service.CurrentChatMessages[1].Text).IsEqualTo("I am AI");
    }

    [Test]
    public async Task TalkTo_RaisesAiRespondedEvent()
    {
        var (service, _, chatClient, _, _, _, _, _) = CreateService(withMessageStore: false);
        SetupStreamingResponse(chatClient, "Response text");

        AiResponse? received = null;
        service.AiResponded += r => received = r;

        await service.TalkTo("Test", CancellationToken.None);

        await Assert.That(received).IsNotNull();
        await Assert.That(received!.Message).IsEqualTo("Response text");
        await Assert.That(received.Timestamp).IsEqualTo(FixedTime);
    }

    [Test]
    public async Task TalkTo_StoresUserAndAiMessages_WhenMessageStoreConfigured()
    {
        var (service, _, chatClient, _, _, _, messageStore, _) = CreateService();
        SetupStreamingResponse(chatClient, "Stored response");

        await service.TalkTo("Stored input", CancellationToken.None);

        messageStore
            .Store(Arg<AiChatMessage>.Any(), Arg<CancellationToken>.Any())
            .Called(Count.Exactly(2));
    }

    [Test]
    public async Task TalkTo_DoesNotCallMessageStore_WhenNotConfigured()
    {
        var (service, _, chatClient, _, _, _, messageStore, _) = CreateService(withMessageStore: false);
        SetupStreamingResponse(chatClient, "No store");

        await service.TalkTo("Test", CancellationToken.None);

        messageStore
            .Store(Arg<AiChatMessage>.Any(), Arg<CancellationToken>.Any())
            .Called(Count.Never());
    }

    [Test]
    public async Task TalkTo_TransitionsThroughCorrectStates()
    {
        var (service, _, chatClient, _, _, _, _, _) = CreateService(withMessageStore: false);
        SetupStreamingResponse(chatClient, "OK");

        var states = new List<AiState>();
        service.StateChanged += () => states.Add(service.Status);

        await service.TalkTo("Test", CancellationToken.None);

        await Assert.That(states).Contains(AiState.Thinking);
        await Assert.That(states).Contains(AiState.Responding);
        await Assert.That(states.Last()).IsEqualTo(AiState.Idle);
    }

    [Test]
    public async Task TalkTo_SpeaksResponse_WhenAcknowledgementIsFull()
    {
        var (service, _, chatClient, _, textToSpeech, _, _, _) = CreateService(withMessageStore: false);
        service.Acknowledgement = AiAcknowledgement.Full;
        SetupStreamingResponse(chatClient, "Speak this");

        await service.TalkTo("Test", CancellationToken.None);

        textToSpeech
            .SpeakAsync(Arg<string>.Any(), Arg<Shiny.Speech.TextToSpeechOptions?>.Any(), Arg<CancellationToken>.Any())
            .Called(Count.AtLeast(1));
    }

    [Test]
    public async Task TalkTo_DoesNotSpeak_WhenAcknowledgementIsNone()
    {
        var (service, _, chatClient, _, textToSpeech, _, _, _) = CreateService(withMessageStore: false);
        service.Acknowledgement = AiAcknowledgement.None;
        SetupStreamingResponse(chatClient, "Silent");

        await service.TalkTo("Test", CancellationToken.None);

        textToSpeech
            .SpeakAsync(Arg<string>.Any(), Arg<Shiny.Speech.TextToSpeechOptions?>.Any(), Arg<CancellationToken>.Any())
            .Called(Count.Never());
    }

    #endregion

    #region System Prompts

    [Test]
    public async Task TalkTo_IncludesSystemPrompts()
    {
        var (service, _, chatClient, _, _, _, _, _) = CreateService(withMessageStore: false);
        service.SystemPrompts.Add("You are a test bot.");

        IEnumerable<ChatMessage>? capturedMessages = null;
        chatClient
            .GetStreamingResponseAsync(
                Arg<IEnumerable<ChatMessage>>.Any(),
                Arg<ChatOptions?>.Any(),
                Arg<CancellationToken>.Any()
            )
            .Returns((messages, _, _) =>
            {
                capturedMessages = messages;
                return ToAsyncEnumerable(CreateUpdate("OK"));
            });

        await service.TalkTo("Hello", CancellationToken.None);

        await Assert.That(capturedMessages).IsNotNull();
        var msgList = capturedMessages!.ToList();
        var systemMessages = msgList.Where(m => m.Role == ChatRole.System).ToList();
        await Assert.That(systemMessages.Any(m => m.Text == "You are a test bot.")).IsTrue();
    }

    [Test]
    public async Task TalkTo_AddsLessWordyPrompt_WhenAcknowledgementIsLessWordy()
    {
        var (service, _, chatClient, _, _, _, _, _) = CreateService(withMessageStore: false);
        service.Acknowledgement = AiAcknowledgement.LessWordy;

        IEnumerable<ChatMessage>? capturedMessages = null;
        chatClient
            .GetStreamingResponseAsync(
                Arg<IEnumerable<ChatMessage>>.Any(),
                Arg<ChatOptions?>.Any(),
                Arg<CancellationToken>.Any()
            )
            .Returns((messages, _, _) =>
            {
                capturedMessages = messages;
                return ToAsyncEnumerable(CreateUpdate("OK"));
            });

        await service.TalkTo("Hello", CancellationToken.None);

        var msgList = capturedMessages!.ToList();
        var systemMessages = msgList.Where(m => m.Role == ChatRole.System).ToList();
        await Assert.That(systemMessages.Any(m => m.Text!.Contains("concise"))).IsTrue();
    }

    #endregion

    #region ClearCurrentChat

    [Test]
    public async Task ClearCurrentChat_RemovesAllMessages()
    {
        var (service, _, chatClient, _, _, _, _, _) = CreateService(withMessageStore: false);
        SetupStreamingResponse(chatClient, "Response");

        await service.TalkTo("Hello", CancellationToken.None);
        await Assert.That(service.CurrentChatMessages.Count).IsEqualTo(2);

        service.ClearCurrentChat();
        await Assert.That(service.CurrentChatMessages.Count).IsEqualTo(0);
    }

    #endregion

    #region GetChatHistory / ClearChatHistory

    [Test]
    public async Task GetChatHistory_ThrowsWhenNoMessageStore()
    {
        var (service, _, _, _, _, _, _, _) = CreateService(withMessageStore: false);

        await Assert.That(() => service.GetChatHistory()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ClearChatHistory_ThrowsWhenNoMessageStore()
    {
        var (service, _, _, _, _, _, _, _) = CreateService(withMessageStore: false);

        await Assert.That(() => service.ClearChatHistory()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task GetChatHistory_DelegatesToMessageStore()
    {
        var (service, _, _, _, _, _, messageStore, _) = CreateService();
        var expected = new List<AiChatMessage>
        {
            new("1", "Hello", FixedTime, ChatMessageDirection.User)
        };

        messageStore
            .Query(
                Arg<string?>.Any(),
                Arg<DateTimeOffset?>.Any(),
                Arg<DateTimeOffset?>.Any(),
                Arg<int?>.Any(),
                Arg<CancellationToken>.Any()
            )
            .ReturnsAsync((IReadOnlyList<AiChatMessage>)expected.AsReadOnly());

        var result = await service.GetChatHistory(limit: 10);
        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Message).IsEqualTo("Hello");
    }

    [Test]
    public async Task ClearChatHistory_DelegatesToMessageStore()
    {
        var (service, _, _, _, _, _, messageStore, _) = CreateService();
        messageStore.Clear(Arg<DateTimeOffset?>.Any()).Returns(Task.CompletedTask);

        await service.ClearChatHistory();

        messageStore.Clear(Arg<DateTimeOffset?>.Any()).Called(Count.Once());
    }

    #endregion

    #region ListenAndTalk

    [Test]
    public async Task ListenAndTalk_ThrowsWhenWakeWordActive()
    {
        var (service, _, chatClient, speechToText, _, _, _, _) = CreateService(withMessageStore: false);

        // ListenForKeyword is an extension that calls ContinuousRecognize internally
        speechToText
            .ContinuousRecognize(Arg<SpeechRecognitionOptions?>.Any(), Arg<CancellationToken>.Any())
            .Returns(EmptyAsyncEnumerable<SpeechRecognitionResult>());

        await service.StartWakeWord("Hey Test");

        await Assert.That(() => service.ListenAndTalk(CancellationToken.None))
            .Throws<InvalidOperationException>();

        service.StopWakeWord();
    }

    #endregion

    #region Wake Word

    [Test]
    public async Task StartWakeWord_SetsWakeWordAndState()
    {
        var (service, _, _, speechToText, _, _, _, _) = CreateService(withMessageStore: false);

        speechToText
            .ContinuousRecognize(Arg<SpeechRecognitionOptions?>.Any(), Arg<CancellationToken>.Any())
            .Returns((_, ct) => HangUntilCancelled<SpeechRecognitionResult>(ct));

        await service.StartWakeWord("Hey Bot");

        await Assert.That(service.IsWakeWordEnabled).IsTrue();
        await Assert.That(service.WakeWord).IsEqualTo("Hey Bot");

        service.StopWakeWord();

        // Wait for background task to clean up
        for (var i = 0; i < 50 && service.IsWakeWordEnabled; i++)
            await Task.Delay(50);

        await Assert.That(service.IsWakeWordEnabled).IsFalse();
        await Assert.That(service.WakeWord).IsNull();
    }

    [Test]
    public async Task StartWakeWord_ThrowsIfAlreadyActive()
    {
        var (service, _, _, speechToText, _, _, _, _) = CreateService(withMessageStore: false);

        speechToText
            .ContinuousRecognize(Arg<SpeechRecognitionOptions?>.Any(), Arg<CancellationToken>.Any())
            .Returns((_, ct) => HangUntilCancelled<SpeechRecognitionResult>(ct));

        await service.StartWakeWord("Hey Bot");

        await Assert.That(() => service.StartWakeWord("Hey Bot"))
            .Throws<InvalidOperationException>();

        service.StopWakeWord();
    }

    #endregion

    #region Helpers

    static void SetupStreamingResponse(IChatClientImposter chatClient, string responseText)
    {
        chatClient
            .GetStreamingResponseAsync(
                Arg<IEnumerable<ChatMessage>>.Any(),
                Arg<ChatOptions?>.Any(),
                Arg<CancellationToken>.Any()
            )
            .Returns(ToAsyncEnumerable(CreateUpdate(responseText)));
    }

    static ChatResponseUpdate CreateUpdate(string text)
    {
        var update = new ChatResponseUpdate();
        update.Contents.Add(new TextContent(text));
        return update;
    }

    static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.CompletedTask;
        }
    }

    static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    static async IAsyncEnumerable<T> HangUntilCancelled<T>(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        yield break;
    }

    #endregion
}

internal class FakeTimeProvider(DateTimeOffset fixedTime) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => fixedTime;
}
