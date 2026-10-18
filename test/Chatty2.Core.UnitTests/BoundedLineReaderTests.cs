using Xunit;

namespace Chatty2.Core.UnitTests;

public class BoundedLineReaderTests
{
    [Theory]
    [InlineData("one\ntwo\n")]
    [InlineData("one\r\ntwo\r\n")]
    [InlineData("one\rtwo\r")]
    public async Task Should_ReturnEachLine_When_LinesAreTerminatedByAnyLineBreakStyle(string input)
    {
        var reader = new BoundedLineReader(new StringReader(input), 100);

        var first = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        var second = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        var third = await reader.ReadLineAsync(TestContext.Current.CancellationToken);

        Assert.Equal("one", first);
        Assert.Equal("two", second);
        Assert.Null(third);
    }

    [Fact]
    public async Task Should_ReturnEmptyString_When_LineIsBlank()
    {
        var reader = new BoundedLineReader(new StringReader("\nafter\n"), 100);

        var blank = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        var next = await reader.ReadLineAsync(TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, blank);
        Assert.Equal("after", next);
    }

    [Fact]
    public async Task Should_ReturnFinalLine_When_StreamEndsWithoutTerminator()
    {
        var reader = new BoundedLineReader(new StringReader("one\ntail"), 100);

        Assert.Equal("one", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Equal("tail", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Null(await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Should_ReturnNull_When_StreamIsEmpty()
    {
        var reader = new BoundedLineReader(new StringReader(string.Empty), 100);

        Assert.Null(await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Should_NotProduceExtraBlankLine_When_CrLfIsSplitAcrossReads()
    {
        // The "\r" ends the first line immediately; the "\n" that arrives in a later read is
        // the second half of the same terminator and must not surface as an empty line.
        var reader = new BoundedLineReader(new OneCharAtATimeReader("a\r\nb\n"), 100);

        Assert.Equal("a", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Equal("b", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Null(await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Should_ReturnLine_When_LengthEqualsTheLimit()
    {
        var line = new string('x', 10);
        var reader = new BoundedLineReader(new StringReader(line + "\n"), 10);

        Assert.Equal(line, await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Should_ReturnLongLine_When_ItSpansSeveralInternalBufferRefillsButFitsTheLimit()
    {
        var line = new string('x', 5000);
        var reader = new BoundedLineReader(new StringReader(line + "\nnext\n"), 6000);

        Assert.Equal(line, await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Equal("next", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Should_ThrowMessageTooLong_AndResumeAtTheNextLine_When_LineExceedsTheLimit()
    {
        var reader = new BoundedLineReader(new StringReader(new string('x', 11) + "\nnext\n"), 10);

        var exception = await Assert.ThrowsAsync<MessageTooLongException>(
            () => reader.ReadLineAsync(TestContext.Current.CancellationToken));

        Assert.Equal(10, exception.MaxLength);
        Assert.Equal("next", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Should_ThrowOnce_AndResumeAtTheNextLine_When_OversizedLineSpansManyBufferRefills()
    {
        var reader = new BoundedLineReader(new StringReader(new string('x', 10_000) + "\r\nnext\n"), 4096);

        await Assert.ThrowsAsync<MessageTooLongException>(
            () => reader.ReadLineAsync(TestContext.Current.CancellationToken));

        Assert.Equal("next", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Null(await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Should_ThrowMessageTooLongThenReturnNull_When_OversizedLineEndsTheStreamWithoutTerminator()
    {
        var reader = new BoundedLineReader(new StringReader(new string('x', 11)), 10);

        await Assert.ThrowsAsync<MessageTooLongException>(
            () => reader.ReadLineAsync(TestContext.Current.CancellationToken));

        Assert.Null(await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Should_ResumeCorrectly_When_OversizedLineIsFollowedByCrLfSplitAcrossReads()
    {
        var reader = new BoundedLineReader(new OneCharAtATimeReader("toolongline\r\nok\n"), 4);

        await Assert.ThrowsAsync<MessageTooLongException>(
            () => reader.ReadLineAsync(TestContext.Current.CancellationToken));

        Assert.Equal("ok", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Should_ThrowOperationCanceled_When_TokenIsAlreadyCancelled()
    {
        var reader = new BoundedLineReader(new StringReader("one\n"), 100);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadLineAsync(cts.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Should_ThrowArgumentOutOfRange_When_LimitIsNotPositive(int maxLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedLineReader(new StringReader("x"), maxLength));
    }

    [Fact]
    public void Should_ThrowArgumentNull_When_ReaderIsNull()
    {
        // null! is deliberate: the test exists to prove the guard rejects what the signature forbids.
        Assert.Throws<ArgumentNullException>(() => new BoundedLineReader(null!, 10));
    }

    // Hands back one character per read, the way a socket may split a terminator or a line
    // across receives, so the reader's carry-over state is exercised deterministically.
    private sealed class OneCharAtATimeReader(string text) : TextReader
    {
        private readonly StringReader _inner = new(text);

        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
