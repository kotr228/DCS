using AsmodayCat.Shared.Ipc;
using AsmodayCat.UI.Localization;

namespace AsmodayCat.UI.Services;

// Switches the interface language, remembers it and tells the service,
// so its messages (errors, notifications, approval requests) use the same language.
public sealed class LanguageService(UiSettingsStore settings, IpcClient ipc)
{
    // Before any window is created
    public void Init() => Loc.Instance.Set(Loc.Normalize(settings.Current.Language));

    public void Apply(string lang)
    {
        Loc.Instance.Set(lang);
        settings.Current.Language = Loc.Instance.Lang;
        settings.Save();
        _ = SyncServiceAsync();
    }

    // The service keeps the language in its database, so this is needed once it runs
    public async Task SyncServiceAsync()
    {
        var cmd = new IpcCommand { Type = IpcCommandType.SetLanguage };
        cmd.Parameters["Lang"] = Loc.Instance.Lang;
        await ipc.SendCommandAsync(cmd);
    }
}
