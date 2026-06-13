namespace AsmodayCat.Shared.Models;

public enum LogEntryLevel { Info, Warning, Error, Agent, System, Network }

public class LogEntryDto
{
    public DateTime      Timestamp { get; set; } = DateTime.Now;
    public LogEntryLevel Level     { get; set; } = LogEntryLevel.Info;
    public string        Message   { get; set; } = string.Empty;
    public string        Source    { get; set; } = string.Empty;
}

public class LogBatchDto
{
    public List<LogEntryDto> Entries    { get; set; } = [];
    public int               NextOffset { get; set; }
}
