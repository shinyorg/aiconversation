using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;
using Shiny.Maui.AiConversation;

namespace Sample.Pages;

public partial class AuraViewModel(IAiConversationService aiService)
    : ObservableObject, IPageLifecycleAware
{
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

    public void OnAppearing()
    {
        aiService.StateChanged += this.OnStateChanged;
        aiService.AiResponded += this.OnAiResponded;
        RefreshAll();
    }

    public void OnDisappearing()
    {
        aiService.StateChanged -= this.OnStateChanged;
        aiService.AiResponded -= this.OnAiResponded;
    }

    void OnAiResponded(AiResponse response)
    {
        if (response.WasReadAloud)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            this.LastResponseText = response.Message;
            this.IsResponseVisible = true;
        });
    }

    void OnStateChanged()
    {
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
