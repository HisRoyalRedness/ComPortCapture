using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HisRoyalRedness.com
{
    public sealed class LineLogger : IDisposable
    {
        enum WriteState
        {
            StartOfLine,
            MiddleOfLine,
            EndOfLine
        }

        public LineLogger(Configuration config)
        {
            if (config.IsLogging)
            {
                _logWriter =
                    new RollableLogWriter(
                        config.LogPath,
                        new Func<string, string>(path => Path.Combine(path, $"{config.COMPort} {DateTime.Now:yyyyMMddHHmmss}.log")));
                _logWriter.MaxLogFileSize = (int)config.LogFileSize;
            }
        }

        public void RollLog() => _logWriter?.RollLogFile();

        public string Header
        {
            get => _logWriter?.HeaderLine ?? string.Empty;
            set
            {
                if (_logWriter != null)
                    _logWriter.HeaderLine = value;
            }
        }

        #region Write
        public void WriteLineRaw(string msg)
        {
            InternalWrite(msg + Environment.NewLine);
        }

        public void WriteLine(string msg) => Write(msg + Environment.NewLine);

        public void Write(string msg)
        {
            foreach (var c in msg)
                Write(c);
        }

        public void Write(char[] buffer, int offset, int length)
        {
            for (var i = 0; i < length; ++i)
                Write(buffer[i + offset]);
        }

        public void Write(char value)
        {
            var hasChar = true;
            while (hasChar)
            {
                switch (_state)
                {
                    case WriteState.StartOfLine:
                        var msg = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff: ");
                        InternalWrite(msg);
                        _state = WriteState.MiddleOfLine;
                        break;

                    case WriteState.MiddleOfLine:

                        switch (value)
                        {
                            case '\r':
                                if (_cr)
                                    _state = WriteState.EndOfLine;
                                _cr = true;
                                hasChar = false; // Consume the current char
                                break;

                            case '\n':
                                _cr = false;
                                _state = WriteState.EndOfLine;
                                hasChar = false; // Consume the current char
                                break;

                            default:
                                if (_cr)
                                {
                                    _state = WriteState.EndOfLine;
                                    _cr = false;
                                }
                                else
                                {
                                    InternalWrite(value.ToString());
                                    hasChar = false; // Consume the current char
                                }
                                break;
                        }
                        break;

                    case WriteState.EndOfLine:
                        InternalWrite(Environment.NewLine);
                        _state = WriteState.StartOfLine;
                        break;
                }
            }
        }

        void InternalWrite(string msg)
        {
            _logWriter?.Write(msg);
            Console.Write(msg);
        }
        #endregion Write

        public void Dispose()
        {
            InternalWrite(Environment.NewLine);
            try
            {
                _logWriter?.Dispose();
            }
            catch (Exception)
            { }
            _logWriter = null;
        }

        RollableLogWriter _logWriter;
        WriteState _state = WriteState.StartOfLine;
        bool _cr = false;
    }
}
