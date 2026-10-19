using System.Diagnostics.Tracing;

namespace Shuush;

[EventSource(Name = "Shuush-Detection")]
internal sealed class DetectionEvents : EventSource
{
    public static readonly DetectionEvents Log = new();

    [Event(2, Level = EventLevel.Verbose)]
    public void ShellWindowCreated(long window)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(2, window);
        }
    }

    [Event(1, Level = EventLevel.Verbose)]
    public void WindowEventReceived(int eventType, long window, int objectId, int childId)
    {
        if (this.IsEnabled())
        {
            this.WriteEvent(1, eventType, window, objectId, childId);
        }
    }
}
