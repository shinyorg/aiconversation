using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;
using Shiny.AiConversation;

namespace Sample.Pages;

public partial class AuraViewModel(IAiConversationService aiService)
    : ObservableObject, IPageLifecycleAware
{
    CancellationTokenSource? listenCts;
    readonly StringBuilder responseBuffer = new();

    public AiState CurrentState => aiService.Status;
    public string StatusText => aiService.Status.ToString();

    public Color AuraColor => aiService.Status switch
    {
        AiState.Listening => Color.FromArgb("#00D2FF"),
        AiState.Thinking => Color.FromArgb("#A29BFE"),
        AiState.Responding => Color.FromArgb("#00B894"),
        _ => Color.FromArgb("#6C5CE7")
    };

    public double AuraOpacity => aiService.Status == AiState.Idle ? 0.3 : 0.8;

    [ObservableProperty]
    string? lastResponseText;

    [ObservableProperty]
    bool isResponseVisible;

    [RelayCommand]
    void DismissResponse() => this.IsResponseVisible = false;

    [RelayCommand(AllowConcurrentExecutions = true)]
    async Task AuraTapped()
    {
        if (aiService.Status == AiState.Idle)
        {
            this.listenCts = new CancellationTokenSource();
            try
            {
                await aiService.ListenAndTalk(this.listenCts.Token);
            }
            catch (InvalidOperationException) { }
            finally
            {
                this.listenCts?.Dispose();
                this.listenCts = null;
            }
        }
        else if (aiService.WakeWord == null)
        {
            this.listenCts?.Cancel();
        }
    }

    public void OnAppearing()
    {
        aiService.StatusChanged += this.OnStatusChanged;
        aiService.AiResponded += this.OnAiResponded;
        RefreshAll();
    }

    public void OnDisappearing()
    {
        aiService.StatusChanged -= this.OnStatusChanged;
        aiService.AiResponded -= this.OnAiResponded;
    }

    void OnAiResponded(AiResponse response)
    {
        if (response.WasReadAloud)
            return;

        if (response.Update.Text is { } text)
            this.responseBuffer.Append(text);

        if (response.IsResponseCompleted)
        {
            var fullText = this.responseBuffer.ToString();
            this.responseBuffer.Clear();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                this.LastResponseText = fullText;
                this.IsResponseVisible = true;
            });
        }
    }

    void OnStatusChanged(AiState state)
    {
        if (state == AiState.Thinking)
            this.responseBuffer.Clear();

        MainThread.BeginInvokeOnMainThread(RefreshAll);
    }

    void RefreshAll()
    {
        OnPropertyChanged(nameof(CurrentState));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(AuraColor));
        OnPropertyChanged(nameof(AuraOpacity));
    }
}
