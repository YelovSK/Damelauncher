using System.Drawing;

namespace Dame.Win32.Input;

internal readonly record struct WindowFileDragEvent(
    WindowFileDragKind Kind,
    PointF Position,
    IReadOnlyList<string> Paths);

internal enum WindowFileDragKind
{
    Entered,
    Moved,
    Dropped,
    Left,
}
