using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;
using Shiny.Maui.AiConversation;
using Shiny.Maui.Controls.Chat;

namespace Sample.Pages;

public partial class ChatViewModel(IAiService aiService, IDialogs dialogs)
    : ObservableObject, IPageLifecycleAware
{
    const int PageSize = 25;
    bool hasMoreHistory = true;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public List<ChatParticipant> Participants { get; } =
    [
        new ChatParticipant
        {
            Id = "copilot",
            DisplayName = "Copilot",
            Avatar = new FontImageSource
            {
                Glyph = "\U0001F916",
                FontFamily = "OpenSansRegular",
                Size = 24,
                Color = Colors.White
            }
        }
    ];

    public async void OnAppearing()
    {
        aiService.AiResponded += this.OnAiResponded;

        if (this.Messages.Count == 0)
            await this.LoadHistory();
    }

    public void OnDisappearing()
    {
        aiService.AiResponded -= this.OnAiResponded;
    }

    void OnAiResponded(AiResponse response)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            this.Messages.Add(new ChatMessage
            {
                Text = response.Message,
                IsFromMe = false,
                SenderId = "copilot",
                Timestamp = response.Timestamp,
                DateSent = response.Timestamp
            });
        });
    }

    async Task LoadHistory()
    {
        try
        {
            var history = await aiService.GetChatHistory(limit: PageSize);
            this.hasMoreHistory = history.Count >= PageSize;

            foreach (var msg in history)
                this.Messages.Add(ToChatMessage(msg));
        }
        catch (Exception ex)
        {
            await dialogs.Alert("Error", ex.Message);
        }
    }

    [RelayCommand]
    async Task LoadMore()
    {
        if (!this.hasMoreHistory || this.Messages.Count == 0)
            return;

        try
        {
            var oldest = this.Messages[0].Timestamp;
            var older = await aiService.GetChatHistory(endDate: oldest.AddTicks(-1), limit: PageSize);
            this.hasMoreHistory = older.Count >= PageSize;

            for (var i = older.Count - 1; i >= 0; i--)
                this.Messages.Insert(0, ToChatMessage(older[i]));
        }
        catch (Exception ex)
        {
            await dialogs.Alert("Error", ex.Message);
        }
    }

    [RelayCommand]
    async Task Send(string? text)
    {
        if (String.IsNullOrWhiteSpace(text))
            return;

        this.Messages.Add(new ChatMessage
        {
            Text = text,
            IsFromMe = true,
            SenderId = "me",
            Timestamp = DateTimeOffset.Now,
            DateSent = DateTimeOffset.Now
        });

        try
        {
            await aiService.TalkTo(text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await dialogs.Alert("Error", ex.Message);
        }
    }

    static ChatMessage ToChatMessage(AiChatMessage msg) => new()
    {
        Text = msg.Message,
        IsFromMe = msg.Direction == ChatMessageDirection.User,
        SenderId = msg.Direction == ChatMessageDirection.User ? "me" : "copilot",
        Timestamp = msg.Timestamp,
        DateSent = msg.Timestamp
    };
}
