using System;
using System.IO.Ports;
using System.Text.RegularExpressions;

namespace HisRoyalRedness.com
{
    public enum FileSizeUnit
    {
        B,
        KB,
        MB,
        GB,
        TB,
        PB
    }

    public static class FileSizeExtensions
    {
        public static bool TryParseFileSize(this string value, out long fileSize)
        {
            long size = 0;
            if (long.TryParse(value, out size))
            {
                fileSize = size;
                return true;
            }

            var match = Regex.Match(value, "B|KB|MB|GB|TB|PB", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var unit = FileSizeUnit.B;

            if (match.Success)
            {
                if (Enum.TryParse<FileSizeUnit>(match.Value.ToUpper(), out unit))
                {
                    if (long.TryParse(value.ToUpper().Replace(unit.ToString(), string.Empty).Trim(), out size))
                    {
                        var fullSize = Math.Pow(1024, (int)unit) * ((double)size);
                        return long.TryParse(fullSize.ToString(), out fileSize);
                    }
                }
            }

            fileSize = 0;
            return false;
        }

        public static string ToFileSize(this int fileSize, int decimalPoints = 2) => ToFileSize((long)fileSize, decimalPoints);

        public static string ToFileSize(this long fileSize, int decimalPoints = 2)
        {
            if (fileSize < 0)
                throw new ArgumentOutOfRangeException(nameof(fileSize), $"{nameof(fileSize)} cannot be negative.");

            var unit = FileSizeUnit.B;
            var len = (Decimal)fileSize;
            while (len > 1024 && unit < FileSizeUnit.PB)
            {
                len /= 1024;
                unit++;
            }

            return len.ToString($"0.{new string('#', decimalPoints)}") + $" {unit}";
        }
    }

    public static class RegexExtensions
    {
        public static bool TryMatch(this Regex regex, string inputString, out Match match)
        {
            match = regex.Match(inputString);
            return match.Success;
        }
    }

    public static class SerialConfigExtensions
    {
        public static bool TryParse(this string inp, out StopBits stopbits)
        {
            switch(inp)
            {
                case "0": stopbits = StopBits.None; return true;
                case "1": stopbits =  StopBits.One; return true;
                case "1.5": stopbits = StopBits.OnePointFive; return true;
                case "2": stopbits = StopBits.Two; return true;
                default: stopbits = StopBits.None; return false;
            }
        }

        public static bool TryParse(this string inp, out Parity parity)
        {
            switch (inp.ToLower())
            {
                case "n": parity = Parity.None; return true;
                case "o": parity = Parity.Odd; return true;
                case "e": parity = Parity.Even; return true;
                case "m": parity = Parity.Mark; return true;
                case "s": parity = Parity.Space; return true;
                default: parity = Parity.None; return false;
            }
        }

        public static bool TryParse(this string inp, out Handshake flow)
        {
            switch (inp.ToLower())
            {
                case "n": flow = Handshake.None; return true;
                case "r": flow = Handshake.RequestToSend; return true;
                case "x": flow = Handshake.XOnXOff; return true;
                case "b": flow = Handshake.RequestToSendXOnXOff; return true;
                default: flow = Handshake.None; return false;
            }
        }

        public static string ToConfigString(this StopBits stopbits)
            => stopbits switch
            {
                StopBits.None => "0",
                StopBits.One => "1",
                StopBits.OnePointFive => "1.5",
                StopBits.Two => "2",
                _ => throw new ArgumentOutOfRangeException(nameof(stopbits)),
            };


        public static string ToConfigString(this Parity parity)
            => parity switch
            {
                Parity.None => "n",
                Parity.Odd => "o",
                Parity.Even => "e",
                Parity.Mark => "m",
                Parity.Space => "s",
                _ => throw new ArgumentOutOfRangeException(nameof(parity)),
            };

        public static string ToConfigString(this Handshake handshake)
            => handshake switch
            {
                Handshake.None => "n",
                Handshake.RequestToSend => "r",
                Handshake.XOnXOff => "x",
                Handshake.RequestToSendXOnXOff => "b",
                _ => throw new ArgumentOutOfRangeException(nameof(handshake)),
            };
    }
}
