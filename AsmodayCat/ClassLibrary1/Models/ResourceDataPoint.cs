namespace AsmodayCat.Shared.Models;

// Value-type like TrafficDataPoint in BlackCat — init-only props allow System.Text.Json round-trip
public readonly struct ResourceDataPoint
{
    public double   Value     { get; init; }
    public DateTime Timestamp { get; init; }

    public ResourceDataPoint(double value, DateTime timestamp)
    {
        Value     = value;
        Timestamp = timestamp;
    }
}
