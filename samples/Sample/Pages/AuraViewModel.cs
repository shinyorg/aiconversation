using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;
using Shiny.AiConversation;

namespace Sample.Pages;

public partial class AuraViewModel(IAiConversationService aiService)
    : ObservableObject, IPageLifecycleAware
{
    CancellationTokenSource? listenCts;

    public string StatusText => aiService.Status.ToString();

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
        OnPropertyChanged(nameof(StatusText));
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

        if (response.Response.Text is { } text)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                this.LastResponseText = text;
                this.IsResponseVisible = true;
            });
        }
    }

    void OnStatusChanged(AiState state)
    {
        MainThread.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(StatusText)));
    }
}
