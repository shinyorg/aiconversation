using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;
using Shiny.Maui.AI;

namespace Sample.Pages;

public partial class SettingsViewModel(IAiService aiService, IDialogs dialogs)
    : ObservableObject, IPageLifecycleAware
{
    public string[] AcknowledgementOptions => Enum.GetNames<AiAcknowledgement>();

    public string SelectedAcknowledgementName
    {
        get => aiService.Acknowledgement.ToString();
        set
        {
            if (Enum.TryParse<AiAcknowledgement>(value, out var parsed))
            {
                aiService.Acknowledgement = parsed;
                OnPropertyChanged();
            }
        }
    }

    [ObservableProperty]
    string wakeWordText = "Hey Copilot";

    public bool IsWakeWordActive => aiService.WakeWord != null;
    public bool IsNotWakeWordActive => !this.IsWakeWordActive;
    public string WakeWordButtonText => this.IsWakeWordActive ? "Stop Wake Word" : "Start Wake Word";
    public string CurrentStatus => aiService.Status.ToString();

    public void OnAppearing()
    {
        aiService.StateChanged += this.OnStateChanged;
        RefreshAll();
    }

    public void OnDisappearing()
    {
        aiService.StateChanged -= this.OnStateChanged;
    }

    void OnStateChanged()
    {
        MainThread.BeginInvokeOnMainThread(RefreshAll);
    }

    void RefreshAll()
    {
        OnPropertyChanged(nameof(IsWakeWordActive));
        OnPropertyChanged(nameof(IsNotWakeWordActive));
        OnPropertyChanged(nameof(WakeWordButtonText));
        OnPropertyChanged(nameof(CurrentStatus));
        OnPropertyChanged(nameof(SelectedAcknowledgementName));
    }

    [RelayCommand]
    async Task ToggleWakeWord()
    {
        try
        {
            if (this.IsWakeWordActive)
            {
                aiService.StopWakeWord();
            }
            else
            {
                if (String.IsNullOrWhiteSpace(this.WakeWordText))
                {
                    await dialogs.Alert("Error", "Please enter a wake word.");
                    return;
                }
                await aiService.StartWakeWord(this.WakeWordText);
            }
        }
        catch (Exception ex)
        {
            await dialogs.Alert("Error", ex.Message);
        }
    }

    [RelayCommand]
    async Task ClearHistory()
    {
        try
        {
            await aiService.ClearChatHistory();
            await dialogs.Alert("Done", "Chat history cleared.");
        }
        catch (Exception ex)
        {
            await dialogs.Alert("Error", ex.Message);
        }
    }
}
