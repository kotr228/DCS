namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>
    /// One labeled tick mark on the timeline ruler. The pixel offset is pre-computed at
    /// generation time (see <see cref="TimelineViewModel"/>) rather than recomputed live,
    /// since the ruler regenerates whenever <see cref="TimelineViewModel.TotalFrames"/> or
    /// <see cref="TimelineViewModel.PixelsPerFrame"/> changes.
    /// </summary>
    public sealed record TimelineRulerTick(double Frame, double PixelOffset)
    {
        public string Label => Frame.ToString("0");
    }
}
