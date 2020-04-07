using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;

namespace HisRoyalRedness.com
{
    public class Configuration
    {
        public const int DEFAULT_BAUD = 115200;
        public const int DEFAULT_DATA_BITS = 8;
        public const StopBits DEFAULT_STOP_BITS = StopBits.One;
        public const Parity DEFAULT_PARITY = Parity.None;
        public const Handshake DEFAULT_FLOW_CONTROL = Handshake.None;
        public const long DEFAULT_FILE_SIZE = 1024 * 1024 * 10; // 10MB

        public string COMPort { get; set; }

        public int BaudRate { get; set; } = DEFAULT_BAUD;

        public int DataBits { get; set; } = DEFAULT_DATA_BITS;

        public StopBits StopBits { get; set; } = DEFAULT_STOP_BITS;

        public Parity Parity { get; set; } = DEFAULT_PARITY;

        public Handshake FlowControl { get; set; } = DEFAULT_FLOW_CONTROL;

        public long LogFileSize { get; set; } = DEFAULT_FILE_SIZE;

        public string LogPath { get; set; }

        public bool IsLogging { get; set; }

        public LineLogger Logger { get; set; }
    }
}
