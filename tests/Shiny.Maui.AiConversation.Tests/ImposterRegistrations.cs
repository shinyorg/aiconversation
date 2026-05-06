using Imposter.Abstractions;
using Microsoft.Extensions.AI;
using Shiny.Maui.AiConversation;
using Shiny.Speech;

[assembly: GenerateImposter(typeof(IChatClientProvider))]
[assembly: GenerateImposter(typeof(IMessageStore))]
[assembly: GenerateImposter(typeof(ISpeechToTextService))]
[assembly: GenerateImposter(typeof(ITextToSpeechService))]
[assembly: GenerateImposter(typeof(IAudioPlayer))]
[assembly: GenerateImposter(typeof(IChatClient))]
