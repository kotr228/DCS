using AsmodayCat.Shared.Interfaces;
using AsmodayCat.Shared.Models;

namespace AsmodayCat.Service;

// Last notifications in memory; the UI asks for the ones newer than what it has seen.
public sealed class NotificationStore : INotificationSink
{
    private const int Keep = 50;

    private readonly List<NotificationDto> _items = [];
    private long _lastId;

    public void Notify(string title, string text, string? openPath = null, bool isError = false)
    {
        lock (_items)
        {
            _items.Add(new NotificationDto { Id = ++_lastId, Title = title, Text = text, OpenPath = openPath, IsError = isError });
            if (_items.Count > Keep) _items.RemoveAt(0);
        }
    }

    // after < 0: the UI just started — tell it where to start, without old messages
    public object GetSince(long after)
    {
        lock (_items)
            return new
            {
                LastId = _lastId,
                Items  = after < 0 ? [] : _items.Where(n => n.Id > after).ToList()
            };
    }
}
