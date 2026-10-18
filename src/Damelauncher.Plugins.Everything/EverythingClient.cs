using System.Buffers.Binary;
using System.Text;
using Dame.Win32;

namespace Damelauncher.Plugins.Everything;

internal readonly record struct EverythingItem(string Name, string FullPath);

/// <summary>
/// Searches through a running Everything's IPC interface: a query goes to its window as
/// <c>WM_COPYDATA</c>, and the results come back the same way to a window of ours.
/// </summary>
/// <remarks>Created and used on a thread with a message loop, which delivers the replies.</remarks>
internal sealed class EverythingClient : IDisposable
{
    // Everything 1.5 alphas can also run as a named instance, whose window carries the name.
    private static readonly string[] WindowClassNames =
    [
        "EVERYTHING_TASKBAR_NOTIFICATION",
        "EVERYTHING_TASKBAR_NOTIFICATION_(1.5a)",
    ];

    // EVERYTHING_IPC_COPYDATA_QUERY2W
    private const nuint QueryDataId = 18;
    private const uint RequestName = 0x1;
    private const uint RequestFullPath = 0x4;
    private const uint SortNameAscending = 1;
    private const int QueryHeaderSize = 7 * sizeof(uint);
    private const int ListHeaderSize = 5 * sizeof(uint);
    private const int ItemHeaderSize = 2 * sizeof(uint);

    private readonly MessageWindow _window;
    private readonly Dictionary<uint, TaskCompletionSource<EverythingItem[]>> _pendingReplies = [];
    private uint _lastReplyId;

    internal EverythingClient() => _window = new MessageWindow(HandleReply);

    /// <returns>The matches in Everything's order, or <see langword="null"/> when Everything isn't running.</returns>
    internal async Task<EverythingItem[]?> SearchAsync(string search, int maxResults, CancellationToken cancellationToken)
    {
        uint replyId = ++_lastReplyId;
        var reply = new TaskCompletionSource<EverythingItem[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingReplies.Add(replyId, reply);
        try
        {
            byte[] request = CreateRequest(search, replyId, maxResults);
            if (!WindowClassNames.Any(className => WindowCopyData.TrySend(className, QueryDataId, request)))
            {
                return null;
            }

            return await reply.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _pendingReplies.Remove(replyId);
        }
    }

    public void Dispose()
    {
        _window.Dispose();
        foreach (TaskCompletionSource<EverythingItem[]> reply in _pendingReplies.Values)
        {
            reply.TrySetCanceled();
        }
    }

    // EVERYTHING_IPC_QUERY2, followed by the search as null-terminated UTF-16.
    private byte[] CreateRequest(string search, uint replyId, int maxResults)
    {
        byte[] request = new byte[QueryHeaderSize + ((search.Length + 1) * sizeof(char))];
        Span<byte> header = request;
        // The protocol only has room for 32 bits of the window handle, which is all a handle uses.
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)_window.Handle);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], replyId);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], (uint)maxResults);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], RequestName | RequestFullPath);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], SortNameAscending);
        Encoding.Unicode.GetBytes(search, request.AsSpan(QueryHeaderSize));
        return request;
    }

    private void HandleReply(nuint dataId, ReadOnlySpan<byte> data)
    {
        if (dataId > uint.MaxValue || !_pendingReplies.TryGetValue((uint)dataId, out TaskCompletionSource<EverythingItem[]>? reply))
        {
            return;
        }

        try
        {
            reply.TrySetResult(ParseReply(data));
        }
        catch (Exception exception)
        {
            // Thrown back into the window procedure, it would take the process down.
            reply.TrySetException(exception);
        }
    }

    // EVERYTHING_IPC_LIST2: a header, an index of items, then each item's requested fields in order.
    private static EverythingItem[] ParseReply(ReadOnlySpan<byte> data)
    {
        int count = checked((int)ReadUInt32(data, 4));
        var items = new EverythingItem[count];
        for (int index = 0; index < count; index++)
        {
            // Each index entry is the item's flags, then where its fields start.
            int offset = checked((int)ReadUInt32(data, ListHeaderSize + (index * ItemHeaderSize) + 4));
            string name = ReadText(data, ref offset);
            string fullPath = ReadText(data, ref offset);
            items[index] = new EverythingItem(name, fullPath);
        }

        return items;
    }

    // A character count, then that many characters and a null terminator.
    private static string ReadText(ReadOnlySpan<byte> data, ref int offset)
    {
        int length = checked((int)ReadUInt32(data, offset));
        offset += sizeof(uint);
        string text = Encoding.Unicode.GetString(data.Slice(offset, length * sizeof(char)));
        offset += (length + 1) * sizeof(char);
        return text;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
}
