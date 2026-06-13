using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AsmodayCat.Shared.Models;

public enum ModelPoolStatus { NotInstalled, Downloading, Ready, Loaded }

public class LlmModelDto : INotifyPropertyChanged
{
    public string Name            { get; set; } = string.Empty;
    public string RecommendedTask { get; set; } = string.Empty;
    public long   VramUsageBytes  { get; set; }

    private ModelPoolStatus _status;
    public ModelPoolStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            Notify();
            Notify(nameof(VramDisplayText));
        }
    }

    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        set { if (_downloadProgress == value) return; _downloadProgress = value; Notify(); }
    }

    // Formatted for display — non-zero only when model is Loaded
    public string VramDisplayText => Status == ModelPoolStatus.Loaded && VramUsageBytes > 0
        ? $"{VramUsageBytes / 1024.0 / 1024.0:F0} MB"
        : "—";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
