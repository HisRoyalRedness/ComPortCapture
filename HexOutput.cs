using System;
using System.Text;

namespace HisRoyalRedness.com
{
    public class HexOutput : IOutputLogger
    {
        public HexOutput(Configuration config)
        {
            _config = config;
            _maxCol = _config.HexColumns - 1;
        }

        public string Write(ReadOnlyMemory<byte> buffer)
        {
            if (buffer.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            var span = buffer.Span;
            for (int i = 0; i < span.Length; ++i)
                WriteByte(span[i], sb);
            return sb.ToString();
        }

        public string Write(byte[] buffer, int offset, int length)
        {
            if (length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < length; ++i)
                WriteByte(buffer[offset + i], sb);
            return sb.ToString();
        }

        void WriteByte(byte b, StringBuilder sb)
        {
            _asciiLine.Append((b < 0x20 || b == 0x7f) ? '.' : (char)b);

            if (_isStartOfLine)
            {
                sb.Append($"{_address:x8}: {b:x2} ");
                ++_currentCol;
                _isStartOfLine = false;
            }

            else if (_currentCol < _maxCol)
            {
                sb.Append($"{b:x2} ");
                ++_currentCol;
            }

            else
            {
                sb.Append($"{b:x2} ; {_asciiLine.ToString()}{Environment.NewLine}");
                _currentCol = 0;
                _isStartOfLine = true;
                _asciiLine.Clear();
            }

            ColumnSpacer(sb);
            ++_address;
        }

        public string Complete()
        {
            if (_isStartOfLine)
                return string.Empty;

            var sb = new StringBuilder();
            while (_currentCol < _maxCol)
            {
                sb.Append($"   ");
                ++_currentCol;
                ColumnSpacer(sb);
            }

            sb.Append($"   ; {_asciiLine.ToString()}{Environment.NewLine}");
            return sb.ToString();
        }

        void ColumnSpacer(StringBuilder sb)
        {
            if (_currentCol > 0)
            {
                if (_currentCol % 4 == 0)
                    sb.Append(' ');
                if (_currentCol % 8 == 0)
                    sb.Append(' ');
                if (_currentCol % 16 == 0)
                    sb.Append(' ');
            }
        }

        StringBuilder _asciiLine = new StringBuilder();
        bool _isStartOfLine = true;
        ulong _address = 0;
        int _currentCol = 0;
        readonly int _maxCol;
        readonly Configuration _config;
    }
}
