using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;

namespace HisRoyalRedness.com
{
    public sealed class SerialWrapper : IDisposable
    {
        public SerialWrapper(Configuration config)
        {
            Config = config;
        }

        public bool Open()
        {
            if (_port == null)
            {
                _port = new SerialPort(Config.COMPort);
                _port.BaudRate = Config.BaudRate;
                _port.DataBits = Config.DataBits;
                _port.StopBits = Config.StopBits;
                _port.Parity = Config.Parity;
                _port.Handshake = Config.FlowControl;
                _port.Open();
                return true;
            }
            else
                return true;
        }

        public void Close()
        {
            if (_port != null)
            {
                try
                {
                    _port.Dispose();
                }
                finally
                {
                    _port = null;
                }
            }
        }

        public int Read(char[] buffer, int offset, int bufferLen)
        {
            if (_port == null || !_port.IsOpen)
                return -1;

            try
            {
                return _port.Read(buffer, offset, bufferLen);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public Configuration Config { get; private set; }


        SerialPort _port = null;

        public void Dispose()
        {
            Close();
        }
    }
}
