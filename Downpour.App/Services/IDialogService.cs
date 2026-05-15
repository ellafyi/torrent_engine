namespace Downpour.App.Services;

public interface IDialogService
{
    Task ShowErrorAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No");
}