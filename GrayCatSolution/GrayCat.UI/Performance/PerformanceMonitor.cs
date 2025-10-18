namespace GrayCat.UI.Performance;

using Microsoft.Extensions.Logging;
using System.Diagnostics;

/// <summary>
/// Монітор продуктивності для відстеження FPS та часу кадрів
/// </summary>
public class PerformanceMonitor
{
    private readonly Stopwatch _frameTimer = new();
    private readonly Queue<long> _frameTimes = new(100);
    private long _frameCount = 0;
    private DateTime _lastSecond = DateTime.Now;
    private int _currentFps = 0;

    public int CurrentFPS => _currentFps;
    public double AverageFrameTime => _frameTimes.Any() ? _frameTimes.Average() : 0;
    public long TotalFrames => _frameCount;

    public void StartFrame()
    {
        _frameTimer.Restart();
    }

    public void EndFrame()
    {
        _frameTimer.Stop();

        // Зберігаємо час кадру
        _frameTimes.Enqueue(_frameTimer.ElapsedMilliseconds);
        if (_frameTimes.Count > 100)
            _frameTimes.Dequeue();

        _frameCount++;

        // Підрахунок FPS кожну секунду
        var now = DateTime.Now;
        if ((now - _lastSecond).TotalSeconds >= 1.0)
        {
            _currentFps = (int)(_frameCount - _frameCount);
            _lastSecond = now;
        }
    }

    public string GetPerformanceReport(int blockCount)
    {
        return $"FPS: {CurrentFPS} | Avg Frame: {AverageFrameTime:F2}ms | Blocks: {blockCount} | Total Frames: {TotalFrames}";
    }

    public void LogPerformance(ILogger logger, int blockCount)
    {
        logger.LogInformation(
            "Performance: FPS={FPS}, AvgFrameTime={FrameTime}ms, Blocks={BlockCount}, TotalFrames={TotalFrames}",
            CurrentFPS, AverageFrameTime, blockCount, TotalFrames);
    }
}