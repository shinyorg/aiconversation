using Shiny.Speech;

namespace Shiny.AiConversation.Infrastructure;

public class DefaultSoundPlayer(IAudioPlayer audioPlayer) : ISoundProvider
{
    public async Task Play(AiAction action)
    {
        var resourceName = action switch
        {
            AiAction.Ok => "ok.mp3",
            AiAction.Think => "think.mp3",
            AiAction.Respond => "responding.mp3",
            AiAction.Cancel => "cancel.mp3",
            AiAction.Error => "error.mp3",
            _ => null
        };

        if (resourceName == null)
            return;

        var assembly = typeof(DefaultSoundPlayer).Assembly;
        var fullResourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(x => x.EndsWith(resourceName));

        if (fullResourceName == null)
            return;

        await using var stream = assembly.GetManifestResourceStream(fullResourceName)!;
        await audioPlayer.PlayAsync(stream);
    }
}