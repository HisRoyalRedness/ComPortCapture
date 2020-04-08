using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HisRoyalRedness.com
{
    public sealed class LineLogger : IDisposable
    {
        public LineLogger(Configuration config)
        {
            _config = config;
            if (_config.IsLogging)
            {
                _logWriter =
                    new RollableLogWriter(
                        _config.LogPath,
                        new Func<string, string>(path => Path.Combine(path, $"{_config.COMPort} {DateTime.Now:yyyyMMddHHmmss}.log")));
                _logWriter.MaxLogFileSize = (int)_config.LogFileSize;
                _logWriter.AutoFlush = true;
            }

            _outputLogger = _config.IsHexMode
                ? (IOutputLogger)new HexOutput(_config)
                : new TextLineOutput(_config);
        }

        public void RollLog()
        {
            _logWriter?.RollLogFile();
            Console.WriteLine($"{Environment.NewLine}Log file rolled over by user");
        }

        public string Header
        {
            get => _logWriter?.HeaderLine ?? string.Empty;
            set
            {
                if (_logWriter != null)
                    _logWriter.HeaderLine = value;
            }
        }

        public void Write(byte[] buffer, int offset, int length)
        {
            var msg = _outputLogger.Write(buffer, offset, length);
            if (msg?.Length > 0)
            {
                _logWriter?.Write(msg);
                Console.Write(msg);
            }
        }

        public void Dispose()
        {
            try
            {
                var msg = _outputLogger?.Complete();
                if (msg?.Length > 0)
                {
                    _logWriter?.Write(msg);
                    Console.Write(msg);
                }
            }
            catch (Exception)

            { }

            try
            {
                _logWriter?.WriteLine();
            }
            catch (Exception)
            { }

            try
            {
                _logWriter?.Dispose();
            }
            catch (Exception)
            { }
            _logWriter = null;
        }

        readonly Configuration _config;
        RollableLogWriter _logWriter;
        readonly IOutputLogger _outputLogger;
    }

    public interface IOutputLogger
    {
        string Write(byte[] buffer, int offset, int length);
        string Complete();
    }
}
