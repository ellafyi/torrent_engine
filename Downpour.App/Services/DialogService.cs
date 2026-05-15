namespace Downpour.App.Services;

public class DialogService : IDialogService
{
    public Task ShowErrorAsync(string title, string message)
    {
        return Shell.Current.DisplayAlertAsync(title, message, "OK");
    }

    public Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No")
    {
        return Shell.Current.DisplayAlertAsync(title, message, accept, cancel);
    }
}