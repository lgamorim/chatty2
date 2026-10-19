using System.Globalization;

namespace Chatty2.App;

/// <summary>
/// Appends a timestamped line per chat message or event to a writer, flushing after each so
/// nothing is lost if the process dies. Write failures propagate; what to do about them is the
/// caller's call.
/// </summary>
public sealed class ChatLog(TextWriter writer, string localName, TimeProvider timeProvider) : IDisposable
{
    public void Sent(string message) => Append($"[{localName}] {message}");

    public void Received(string peerName, string message) => Append($"[{peerName}] {message}");

    public void Event(string text) => Append($"* {text}");

    public void Dispose() => writer.Dispose();

    private void Append(string entry)
    {
        // The offset matters: the file is append-only and may span days and time zones.
        var timestamp = timeProvider.GetLocalNow().ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        writer.WriteLine($"{timestamp} {entry}");
        writer.Flush();
    }
}
