using System.Text;

namespace Chatty2.Core;

/// <summary>
/// Reads lines like <see cref="TextReader.ReadLineAsync(CancellationToken)"/> (same <c>\r</c>,
/// <c>\n</c> and <c>\r\n</c> terminators) but never holds more than <c>maxLength</c> characters of
/// one line in memory.
/// </summary>
public sealed class BoundedLineReader
{
    private const int BufferSize = 1024;

    private readonly TextReader _reader;
    private readonly int _maxLength;
    private readonly char[] _buffer = new char[BufferSize];
    private int _position;
    private int _length;
    private bool _skipLineFeed;

    public BoundedLineReader(TextReader reader, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        _reader = reader;
        _maxLength = maxLength;
    }

    /// <summary>
    /// Returns the next line, or <c>null</c> at end of stream. A line longer than the limit is
    /// discarded up to and including its terminator and reported by throwing
    /// <see cref="MessageTooLongException"/>; the next call continues with the following line.
    /// </summary>
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        StringBuilder? line = null;
        var accumulated = 0;
        var overflowed = false;

        while (true)
        {
            if (_position == _length)
            {
                _length = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken);
                _position = 0;

                if (_length == 0)
                {
                    if (overflowed) throw new MessageTooLongException(_maxLength);
                    return line?.ToString();
                }
            }

            // A "\r" ends its line as soon as it is seen, without waiting for a possible "\n"
            // that may arrive in a later read; remember to swallow that "\n" when it does.
            if (_skipLineFeed)
            {
                _skipLineFeed = false;
                if (_buffer[_position] == '\n')
                {
                    _position++;
                    continue;
                }
            }

            var pending = _buffer.AsSpan(_position, _length - _position);
            var terminator = pending.IndexOfAny('\r', '\n');
            var chunk = terminator < 0 ? pending : pending[..terminator];

            if (!overflowed)
            {
                if (accumulated + chunk.Length > _maxLength)
                {
                    // Stop accumulating, keep consuming to the terminator so the stream
                    // resynchronises on the next line.
                    overflowed = true;
                    line = null;
                }
                else if (chunk.Length > 0)
                {
                    (line ??= new StringBuilder()).Append(chunk);
                    accumulated += chunk.Length;
                }
            }

            if (terminator < 0)
            {
                _position = _length;
                continue;
            }

            _skipLineFeed = pending[terminator] == '\r';
            _position += terminator + 1;

            if (overflowed) throw new MessageTooLongException(_maxLength);
            return line?.ToString() ?? string.Empty;
        }
    }
}
