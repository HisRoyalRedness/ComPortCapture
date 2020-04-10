using System;
using System.IO.Ports;
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
        public SerialWrapper(Configuration config)
        {
            _config = config;
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
                        Handshake = _config.FlowControl
                    };
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

        public int Read(byte[] buffer, int offset, int bufferLen)
        {
            if (_port?.IsOpen ?? false)
            {
                try
                {
                    return _port.Read(buffer, offset, bufferLen);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: Error reading serial port. {ex.Message}");
                }
            }
            return -1;
        }

        public async ValueTask<ReadResult> ReadAsync(CancellationToken cancelToken = default)
        {
            if (_port?.IsOpen ?? false)
            {
                await _semaphore.WaitAsync(cancelToken);
                {
                    try
                    {
                        var bytesRead = await _port.BaseStream.ReadAsync(_buffer, 0, _buffer.Length, cancelToken);
                        if (bytesRead >= 0)
                        {
                            var buffer = bytesRead == 0
                                ? ReadOnlyMemory<byte>.Empty
                                : new ReadOnlyMemory<byte>(_buffer, 0, bytesRead);
                            return new ReadResult(buffer, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR: Error reading serial port. {ex.Message}");
                    }
                    finally 
                    {
                        _semaphore.Release();
                    }
                }
            }
            return new ReadResult(ReadOnlyMemory<byte>.Empty, false);
        }

        public void Close()
        {
            try
            {
                _port?.Dispose();
            }
            catch { }
            _port = null;
        }

        public void Dispose()
        {
            Close();
        }

        public struct ReadResult
        {
            public ReadResult(ReadOnlyMemory<byte> buffer, bool isReadValid)
            {
                Buffer = buffer;
                IsReadValid = isReadValid;
            }

            public ReadOnlyMemory<byte> Buffer { get; private set; }
            public bool IsReadValid { get; set; }
        }

        readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        byte[] _buffer = new byte[1024];
        SerialPort _port = null;
        readonly Configuration _config;
    }
}
