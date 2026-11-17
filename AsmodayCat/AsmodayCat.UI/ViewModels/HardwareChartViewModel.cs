using AsmodayCat.UI.Localization;
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
                Title          = Loc.T("chart.cpu"),
                Values         = CpuValues,
                Stroke         = new SolidColorBrush(Color.FromRgb(174, 133, 48)),
                Fill           = new SolidColorBrush(Color.FromArgb(40, 174, 133, 48)),
                PointGeometry  = null,
                LineSmoothness = 0.4,
            },
            new LineSeries
            {
                Title          = Loc.T("chart.gpu"),
                Values         = GpuValues,
                Stroke         = new SolidColorBrush(Color.FromRgb(110, 101, 76)),
                Fill           = new SolidColorBrush(Color.FromArgb(40, 110, 101, 76)),
                PointGeometry  = null,
                LineSmoothness = 0.4,
            },
        };

        MemorySeries = new SeriesCollection
        {
            new LineSeries
            {
                Title          = Loc.T("chart.ramFree"),
                Values         = RamValues,
                Stroke         = new SolidColorBrush(Color.FromRgb(79, 138, 74)),
                Fill           = new SolidColorBrush(Color.FromArgb(40, 79, 138, 74)),
                PointGeometry  = null,
                LineSmoothness = 0.4,
            },
            new LineSeries
            {
                Title          = Loc.T("chart.vramUsed"),
                Values         = VramValues,
                Stroke         = new SolidColorBrush(Color.FromRgb(181, 101, 74)),
                Fill           = new SolidColorBrush(Color.FromArgb(40, 181, 101, 74)),
                PointGeometry  = null,
                LineSmoothness = 0.4,
            },
        };

        // Legend follows the interface language
        Tr((Series)CpuGpuSeries[0], "chart.cpu");
        Tr((Series)CpuGpuSeries[1], "chart.gpu");
        Tr((Series)MemorySeries[0], "chart.ramFree");
        Tr((Series)MemorySeries[1], "chart.vramUsed");
    }

    private static void Tr(Series series, string key) =>
        System.Windows.Data.BindingOperations.SetBinding(series, Series.TitleProperty,
            new System.Windows.Data.Binding($"[{key}]") { Source = Loc.Instance });

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
