namespace AsmodayCat.Shared.Interfaces;

// Recent agent activity shown on the dashboard.
public interface IActivityLog
{
    void LogActivity(string description, string status);
}
