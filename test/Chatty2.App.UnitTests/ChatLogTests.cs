using System.Globalization;
using System.Text;
using Xunit;

namespace Chatty2.App.UnitTests;

public class ChatLogTests
{
    private static readonly DateTimeOffset FixedInstant = new(2026, 8, 4, 14, 30, 5, TimeSpan.Zero);

    [Fact]
    public void Should_WriteTimestampedSentLine_When_MessageIsSent()
    {
        var writer = new StringWriter();
        var log = new ChatLog(writer, "Alice", new FixedTimeProvider(FixedInstant));

        log.Sent("hello there");

        Assert.Equal("2026-08-04T14:30:05+00:00 [Alice] hello there" + writer.NewLine, writer.ToString());
    }

    [Fact]
    public void Should_WriteTimestampedReceivedLineLabelledWithThePeer_When_MessageIsReceived()
    {
        var writer = new StringWriter();
        var log = new ChatLog(writer, "Alice", new FixedTimeProvider(FixedInstant));

        log.Received("Bob", "hi");

        Assert.Equal("2026-08-04T14:30:05+00:00 [Bob] hi" + writer.NewLine, writer.ToString());
    }

    [Fact]
    public void Should_WriteTimestampedEventLine_When_EventIsRecorded()
    {
        var writer = new StringWriter();
        var log = new ChatLog(writer, "Alice", new FixedTimeProvider(FixedInstant));

        log.Event("Bob disconnected.");

        Assert.Equal("2026-08-04T14:30:05+00:00 * Bob disconnected." + writer.NewLine, writer.ToString());
    }

    [Fact]
    public void Should_IncludeTheLocalUtcOffset_When_TheLocalTimeZoneIsNotUtc()
    {
        // The file is append-only across days and possibly across time zones, so each line
        // must say which offset its wall-clock time is in.
        var plusTwo = TimeZoneInfo.CreateCustomTimeZone("plus-two", TimeSpan.FromHours(2), "plus-two", "plus-two");
        var writer = new StringWriter();
        var log = new ChatLog(writer, "Alice", new FixedTimeProvider(FixedInstant, plusTwo));

        log.Sent("hello");

        Assert.Equal("2026-08-04T16:30:05+02:00 [Alice] hello" + writer.NewLine, writer.ToString());
    }

    [Fact]
    public void Should_FormatTimestampInvariantly_When_CurrentCultureUsesADifferentTimeSeparator()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");
            var writer = new StringWriter();
            var log = new ChatLog(writer, "Alice", new FixedTimeProvider(FixedInstant));

            log.Sent("hello");

            Assert.Equal("2026-08-04T14:30:05+00:00 [Alice] hello" + writer.NewLine, writer.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Should_AppendEntriesInOrder_When_SeveralAreRecorded()
    {
        var writer = new StringWriter();
        var log = new ChatLog(writer, "Alice", new FixedTimeProvider(FixedInstant));

        log.Event("Connected to 10.0.0.2:53000.");
        log.Sent("hi");
        log.Received("Bob", "hello");

        Assert.Equal(
            [
                "2026-08-04T14:30:05+00:00 * Connected to 10.0.0.2:53000.",
                "2026-08-04T14:30:05+00:00 [Alice] hi",
                "2026-08-04T14:30:05+00:00 [Bob] hello"
            ],
            writer.ToString().Split(writer.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Should_FlushAfterEveryEntry_When_EntriesAreRecorded()
    {
        // An entry must reach the file as soon as it is written, so a crash or a tail -f
        // never misses the last lines.
        var writer = new FlushCountingWriter();
        var log = new ChatLog(writer, "Alice", new FixedTimeProvider(FixedInstant));

        log.Sent("one");
        log.Received("Bob", "two");
        log.Event("three");

        Assert.Equal(3, writer.FlushCount);
    }

    [Fact]
    public void Should_DisposeTheWriter_When_Disposed()
    {
        var writer = new FlushCountingWriter();
        var log = new ChatLog(writer, "Alice", new FixedTimeProvider(FixedInstant));

        log.Dispose();

        Assert.True(writer.IsDisposed);
    }

    [Fact]
    public void Should_Propagate_When_TheWriterFails()
    {
        // The runner owns the policy for a failing log (report once, stop logging); the
        // log itself must not swallow the failure.
        var log = new ChatLog(new ThrowingWriter(), "Alice", new FixedTimeProvider(FixedInstant));

        Assert.Throws<IOException>(() => log.Sent("hello"));
    }

    private sealed class FlushCountingWriter : StringWriter
    {
        public int FlushCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public override void Flush()
        {
            FlushCount++;
            base.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value) => throw new IOException("disk full");
    }
}
