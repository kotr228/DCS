namespace AsmodayCat.Shared.Models;

public class NotificationDto
{
    public long     Id       { get; set; }
    public string   Title    { get; set; } = string.Empty;
    public string   Text     { get; set; } = string.Empty;
    public string?  OpenPath { get; set; }   // file to open when the notification is clicked
    public bool     IsError  { get; set; }
    public DateTime Time     { get; set; } = DateTime.Now;
}
