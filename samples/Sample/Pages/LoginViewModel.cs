using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sample.Services;
using Shiny;

namespace Sample.Pages;

public partial class LoginViewModel(
    GitHubCopilotChatClientProvider copilot,
    INavigator navigator,
    IDialogs dialogs
) : ObservableObject
{
    CancellationTokenSource? pollCts;

    public TaskCompletionSource? AuthenticationCompleted { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotWaiting))]
    bool isWaiting;

    [ObservableProperty]
    string? userCode;

    public bool IsNotWaiting => !this.IsWaiting;

    [RelayCommand]
    async Task SignIn()
    {
        try
        {
            var deviceCode = await copilot.StartDeviceFlow();
            this.UserCode = deviceCode.UserCode;
            this.IsWaiting = true;

            await Clipboard.Default.SetTextAsync(deviceCode.UserCode);
            await Browser.Default.OpenAsync(deviceCode.VerificationUri, BrowserLaunchMode.SystemPreferred);

            this.pollCts = new CancellationTokenSource(TimeSpan.FromSeconds(deviceCode.ExpiresIn));
            var ct = this.pollCts.Token;
            var interval = TimeSpan.FromSeconds(deviceCode.Interval);

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, ct);

                if (await copilot.PollForToken(deviceCode, ct))
                {
                    this.AuthenticationCompleted?.TrySetResult();
                    await navigator.GoBack();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            this.AuthenticationCompleted?.TrySetCanceled();
            await dialogs.Alert("Timeout", "Authorization timed out. Please try again.");
        }
        catch (Exception ex)
        {
            this.AuthenticationCompleted?.TrySetException(ex);
            await dialogs.Alert("Error", ex.Message);
        }
        finally
        {
            this.IsWaiting = false;
            this.pollCts?.Dispose();
            this.pollCts = null;
        }
    }
}
