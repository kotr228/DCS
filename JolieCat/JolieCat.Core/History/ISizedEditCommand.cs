namespace JolieCat.Core.History
{
    /// <summary>An edit command that knows roughly how much memory it holds, so
    /// <see cref="HistoryManager"/> can cap history by memory instead of only by count.</summary>
    public interface ISizedEditCommand
    {
        long ByteSize { get; }
    }
}
