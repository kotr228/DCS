using System.Windows.Media;
using AsmodayCat.Shared.Models;
using LiveCharts;
using LiveCharts.Wpf;

namespace AsmodayCat.UI.ViewModels;

public sealed class HardwareChartViewModel
{
    private const int MaxPoints = 60;

    public ChartValues<double> CpuValues  { get; } = new();
    public ChartValues<double> GpuValues  { get; } = new();
    public ChartValues<double> RamValues  { get; } = new();
    public ChartValues<double> VramValues { get; } = new();

    public SeriesCollection CpuGpuSeries { get; }
    public SeriesCollection MemorySeries { get; }

    public HardwareChartViewModel()
    {
        CpuGpuSeries = new SeriesCollection
        {
            new LineSeries
            {
                Title          = "CPU %",
                Values         = CpuValues,
                Stroke         = new SolidColorBrush(Color.FromRgb(179, 157, 219)),
                Fill           = new SolidColorBrush(Color.FromArgb(40, 179, 157, 219)),
                PointGeometry  = null,
                LineSmoothness = 0.4,
            },
            new LineSeries
            {
                Title          = "GPU %",
                Values         = GpuValues,
                Stroke         = new SolidColorBrush(Color.FromRgb(239, 83, 80)),
                Fill           = new SolidColorBrush(Color.FromArgb(40, 239, 83, 80)),
                PointGeometry  = null,
                LineSmoothness = 0.4,
            },
        };

        MemorySeries = new SeriesCollection
        {
            new LineSeries
            {
                Title          = "RAM MB",
                Values         = RamValues,
                Stroke         = new SolidColorBrush(Color.FromRgb(102, 187, 106)),
                Fill           = new SolidColorBrush(Color.FromArgb(40, 102, 187, 106)),
                PointGeometry  = null,
                LineSmoothness = 0.4,
            },
            new LineSeries
            {
                Title          = "VRAM MB",
                Values         = VramValues,
                Stroke         = new SolidColorBrush(Color.FromRgb(255, 183, 77)),
                Fill           = new SolidColorBrush(Color.FromArgb(40, 255, 183, 77)),
                PointGeometry  = null,
                LineSmoothness = 0.4,
            },
        };
    }

    // Called from UI thread via DashboardViewModel's DispatcherTimer
    public void UpdateFromDto(SystemStatusDto dto)
    {
        Sync(CpuValues,  dto.CpuHistory);
        Sync(GpuValues,  dto.GpuHistory);
        Sync(RamValues,  dto.RamHistory);
        Sync(VramValues, dto.VramHistory);
    }

    private static void Sync(ChartValues<double> target, List<ResourceDataPoint> source)
    {
        target.Clear();
        foreach (var p in source)
            target.Add(p.Value);
    }
}
