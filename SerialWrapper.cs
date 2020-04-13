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

        public async ValueTask<ReadResult> ReadAsync(CancellationToken cancelToken = default)
        {
            if (_port?.IsOpen ?? false)
            {
                await _semaphore.WaitAsync(cancelToken);
                {
                    try
                    {
                        byte[] buffer = new byte[BUFFER_SIZE];
                        var bytesRead = await _port.BaseStream.ReadAsync(buffer, 0, buffer.Length, cancelToken);
                        if (bytesRead >= 0)
                        {
                            var mem = bytesRead == 0
                                ? ReadOnlyMemory<byte>.Empty
                                : new ReadOnlyMemory<byte>(buffer, 0, bytesRead);
                            return new ReadResult(mem, true);
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

        public ReadResult Read()
        {
            if (_port?.IsOpen ?? false)
            {
                try
                {
                    byte[] buffer = new byte[BUFFER_SIZE];
                    var bytesRead = _port.Read(buffer, 0, buffer.Length);
                    if (bytesRead >= 0)
                    {
                        var mem = bytesRead == 0
                            ? ReadOnlyMemory<byte>.Empty
                            : new ReadOnlyMemory<byte>(buffer, 0, bytesRead);
                        return new ReadResult(mem, true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: Error reading serial port. {ex.Message}");
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

        const int BUFFER_SIZE = 1024;
        readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        SerialPort _port = null;
        readonly Configuration _config;
    }
}
