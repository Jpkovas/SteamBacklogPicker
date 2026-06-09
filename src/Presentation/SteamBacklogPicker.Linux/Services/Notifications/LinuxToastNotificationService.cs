using Domain;
using SteamBacklogPicker.UI.Services.Notifications;
using Tmds.DBus.Protocol;

namespace SteamBacklogPicker.Linux.Services.Notifications;

public sealed class LinuxToastNotificationService : IToastNotificationService
{
    private readonly IFreedesktopNotificationClient _notificationClient;

    public LinuxToastNotificationService(IFreedesktopNotificationClient notificationClient)
    {
        _notificationClient = notificationClient ?? throw new ArgumentNullException(nameof(notificationClient));
    }

    public void ShowGameSelected(GameEntry game, string? imagePath)
    {
        ArgumentNullException.ThrowIfNull(game);

        try
        {
            _notificationClient.Show(
                "Steam Backlog Picker",
                game.Title,
                string.Empty,
                imagePath,
                TimeSpan.FromMilliseconds(1500));
        }
        catch
        {
            // Optional feature in environments without Freedesktop notification support.
        }
    }
}

public interface IFreedesktopNotificationClient
{
    void Show(string appName, string summary, string body, string? imagePath, TimeSpan timeout);
}

public sealed class FreedesktopNotificationClient : IFreedesktopNotificationClient
{
    private const string ServiceName = "org.freedesktop.Notifications";
    private const string ObjectPath = "/org/freedesktop/Notifications";
    private const string InterfaceName = "org.freedesktop.Notifications";
    private const string MethodName = "Notify";
    private const string NotifySignature = "susssasa{sv}i";
    private const int ExpireTimeoutMilliseconds = 5000;

    public void Show(string appName, string summary, string body, string? imagePath, TimeSpan timeout)
    {
        using var connection = new Connection(Address.Session!);
        var connectTask = connection.ConnectAsync().AsTask();
        if (!connectTask.Wait(timeout))
        {
            return;
        }

        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            ServiceName,
            ObjectPath,
            InterfaceName,
            MethodName,
            NotifySignature,
            MessageFlags.None);

        writer.WriteString(appName);
        writer.WriteUInt32(0);
        writer.WriteString(string.IsNullOrWhiteSpace(imagePath) ? string.Empty : imagePath);
        writer.WriteString(summary);
        writer.WriteString(body);
        writer.WriteArray(Array.Empty<string>());
        writer.WriteDictionary(Array.Empty<KeyValuePair<string, VariantValue>>());
        writer.WriteInt32(ExpireTimeoutMilliseconds);

        _ = connection.CallMethodAsync(writer.CreateMessage()).Wait(timeout);
    }
}
