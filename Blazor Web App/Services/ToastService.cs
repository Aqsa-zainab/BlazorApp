namespace BlazorWebApp.Services;

public enum ToastLevel { Info, Success, Warning, Error }

public class ToastService
{
    public event Action<string, ToastLevel>? OnShow;
    public event Action<string, ToastLevel, string, Func<Task>>? OnShowWithAction;

    public void ShowToast(string message, ToastLevel level = ToastLevel.Info)
        => OnShow?.Invoke(message, level);

    public void ShowToastWithAction(string message, ToastLevel level,
                                    string actionLabel, Func<Task> action)
        => OnShowWithAction?.Invoke(message, level, actionLabel, action);
}