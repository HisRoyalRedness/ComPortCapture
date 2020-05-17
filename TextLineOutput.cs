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

                        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff: "));
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
                                sb.Append(value);
                                hasChar = false; // Consume the current char
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
            EndOfLine
        }

        readonly Configuration _config;
        WriteState _state = WriteState.StartOfLine;
    }
}
