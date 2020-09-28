using System;
using System.Text;

namespace HisRoyalRedness.com
{
    public sealed class TextLineOutput : IOutputLogger
    {
        public TextLineOutput(Configuration config)
        {
            _config = config;
        }

        public string Write(ReadOnlyMemory<byte> buffer)
        {
            if (buffer.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var c in Encoding.UTF8.GetString(buffer.Span))
                Write(c, sb);
            return sb.ToString();
        }

        public string Write(byte[] buffer, int offset, int length)
        {
            if (length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var c in Encoding.UTF8.GetString(buffer, offset, length))
                Write(c, sb);
            return sb.ToString();
        }

        void Write(char value, StringBuilder sb)
        {
            var hasChar = true;
            while (hasChar)
            {
                switch (_state)
                {
                    case WriteState.StartOfLine:
                        // If we're ignoring empty lines, discard all newline chars at the front of a line
                        if ((value == '\r' || value == '\n') && _config.IgnoreEmptyLines)
                        {
                            hasChar = false;
                            break;
                        }

                        var dateStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff: ");
                        _lineLen = dateStr.Length;
                        sb.Append(dateStr);
                        _state = WriteState.MiddleOfLine;
                        break;

                    case WriteState.MiddleOfLine:

                        switch (value)
                        {
                            case '\r':
                            case '\n':
                                _state = WriteState.EndOfLine;
                                hasChar = false; // Consume the current char
                                break;

                            default:
                                // Must we wrap the line?
                                if (_config.LineWrap && _lineLen >= _MIN_WRAP_WIDTH && _lineLen >= Console.WindowWidth - 1)
                                {
                                    _state = WriteState.EndOfLine;
                                }
                                else
                                {
                                    hasChar = false; // Consume the current char
                                    sb.Append(value);
                                    ++_lineLen;
                                }
                                break;
                        }
                        break;

                    case WriteState.EndOfLine:
                        sb.Append(Environment.NewLine);
                        _state = WriteState.StartOfLine;
                        break;
                }
            }
        }

        public string Complete() => string.Empty;

        enum WriteState
        {
            StartOfLine,
            MiddleOfLine,
            EndOfLine,
        }

        int _lineLen = 0;
        const int _MIN_WRAP_WIDTH = 30; // Don't wrap smaller than this. 
        readonly Configuration _config;
        WriteState _state = WriteState.StartOfLine;
    }
}
