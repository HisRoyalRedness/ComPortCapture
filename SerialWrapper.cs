using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HisRoyalRedness.com
{
    /// <summary>
    /// Wrap SerialPort operations to make creation and configuration a bit easier
    /// and simplify some error handling
    /// </summary>
    public sealed class SerialWrapper : IDisposable
    {
        public SerialWrapper(Configuration config, ConcurrentCircularBuffer<byte> dataQueue, int bufferSize = DEFAULT_BUFFER_SIZE)
        {
            if (bufferSize <= 0)
                throw new ArgumentOutOfRangeException($"{nameof(bufferSize)} must be larger than zero.");
            _bufferSize = bufferSize;
            _config = config;

            Buffer = dataQueue;
        }

        public bool Open()
        {
            if (_port == null)
            {
                try
                {
                    _port = new SerialPort(_config.COMPort)
                    {
                        BaudRate = _config.BaudRate,
                        DataBits = _config.DataBits,
                        StopBits = _config.StopBits,
                        Parity = _config.Parity,
                        Handshake = _config.FlowControl,
                    };
                    if (_port.ReadBufferSize < _bufferSize)
                        _port.ReadBufferSize = _bufferSize;
                    _port.Open();
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"ERROR: Cannot open {_config.COMPort}. {ex.Message}");
                    return false;
                }
            }
            return true;
        }

        public async Task<int> ReadAsync(CancellationToken cancelToken = default)
        {
            if (_port?.IsOpen ?? false)
            {
                var buffer = new byte[_bufferSize];
                try
                {
                    var bytesRead = await _port.BaseStream.ReadAsync(buffer, 0, buffer.Length, cancelToken);
                    if (bytesRead >= 0)
                    {
                        Buffer.Write(buffer, 0, bytesRead);
                        return bytesRead;
                    }
                }
                catch (OperationCanceledException)
                {
                    // I don't want exceptions for cancelled tasks
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: Error reading serial port. {ex.Message}");
                }
            }
            return 0;
        }

        public int Read()
        {
            if (_port?.IsOpen ?? false)
            {
                var buffer = new byte[_bufferSize];
                try
                {
                    var bytesRead = _port.Read(buffer, 0, buffer.Length);
                    if (bytesRead >= 0)
                    {
                        Buffer.Write(buffer, 0, bytesRead);
                        return bytesRead;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: Error reading serial port. {ex.Message}");
                }
            }
            return 0;
        }

        public Task WriteAsync(byte[] buffer, int offset, int length)
            => (_port?.IsOpen ?? false)
                ? _port.BaseStream.WriteAsync(buffer, offset, length)
                : Task.CompletedTask;

        public void Write(byte[] buffer, int offset, int length)
        {
            if (_port?.IsOpen ?? false)
                _port.Write(buffer, offset, length);
        }

        public ConcurrentCircularBuffer<byte> Buffer { get; }

        public void Close()
        {
            try
            {
                _port?.Dispose();
            }
            catch { }
            _port = null;
        }

        public void Dispose() => Close();



        const int DEFAULT_BUFFER_SIZE = 1024;

        readonly int _bufferSize = DEFAULT_BUFFER_SIZE;
        SerialPort _port = null;
        readonly Configuration _config;
    }
}
