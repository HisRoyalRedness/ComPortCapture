using System;
using System.Collections.Generic;
using System.Text;

namespace HisRoyalRedness.com
{
    public sealed class InputFilter : IDisposable
    {
        public InputFilter(Configuration config)
        {
            _config = config;
        }

        // Not fully implemented yet. The intention with this class was to allow the user of the app
        // to specify  some sort of input encoding. The stream from the serial port would then
        // be decoded into a stream of unicode characters, which would then be re-encoded into 
        // utf-8. 
        // This would achieve the goal of compressing multi-byte characters (if using such an encoding),
        // replace invalid encodings with 0xfffd (the replacement character �), and also pause and wait
        // for subsequent data is only a portion of a multi-byte character has been received.
        //
        // For know, lets assume we'll only be receiving ascii, and pass all data on as-is

        public int Filter(byte[] buffer, int offset, int length, Action<byte[], int, int> writeAction)
        {
            writeAction(buffer, offset, length);
            return length;
        }

        public void Dispose()
        {
            
        }

        readonly Configuration _config;
    }
}
