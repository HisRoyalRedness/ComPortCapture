﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Xml.Linq;

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
        public const int DEFAULT_HEXCOLS = 16;

        public string COMPort { get; set; }

        public int BaudRate { get; set; } = DEFAULT_BAUD;

        public int DataBits { get; set; } = DEFAULT_DATA_BITS;

        public StopBits StopBits { get; set; } = DEFAULT_STOP_BITS;

        public Parity Parity { get; set; } = DEFAULT_PARITY;

        public Handshake FlowControl { get; set; } = DEFAULT_FLOW_CONTROL;

        public long LogFileSize { get; set; } = DEFAULT_FILE_SIZE;

        public string LogPath { get; set; }

        public string BinLogPath { get; set; }

        public bool IsLogging { get; set; }

        public bool IsBinaryLogging { get; set; }

        public bool IgnoreEmptyLines { get; set; }

        public bool IsHexMode { get; set; }

        public bool AllowKeyEntry { get; set; }

        public bool LineWrap { get; set; }

        public bool EnumeratePorts { get; set; }

        public string SaveName { get; set; }

        public string LoadName { get; set; }

        public int HexColumns { get; set; } = DEFAULT_HEXCOLS;

        public Encoding InputEncoding { get; set; } = null;

        public LineLogger Logger { get; set; }
        public IMessageLogger MsgLogger { get; set; }
    }

    #region ConfigExtensions
    internal static class ConfigExtensions
    {
        public static bool Save(this Configuration config, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Ver=1");
            sb.AppendLine($"COMPort={config.COMPort}");
            sb.AppendLine($"BaudRate={config.BaudRate}");
            sb.AppendLine($"DataBits={config.DataBits}");
            sb.AppendLine($"StopBits={(int)config.StopBits}");
            sb.AppendLine($"Parity={(int)config.Parity}");
            sb.AppendLine($"FlowControl={(int)config.FlowControl}");
            sb.AppendLine($"LogFileSize={config.LogFileSize}");
            sb.AppendLine($"LogPath={config.LogPath}");
            sb.AppendLine($"IsLogging={(config.IsLogging ? 1 : 0)}");
            sb.AppendLine($"IsBinaryLogging={(config.IsBinaryLogging ? 1 : 0)}");
            sb.AppendLine($"IgnoreEmptyLines={(config.IgnoreEmptyLines ? 1 : 0)}");
            sb.AppendLine($"IsHexMode={(config.IsHexMode ? 1 : 0)}");
            sb.AppendLine($"AllowKeyEntry={(config.AllowKeyEntry ? 1 : 0)}");
            sb.AppendLine($"LineWrap={(config.LineWrap ? 1 : 0)}");
            sb.AppendLine($"EnumeratePorts={(config.EnumeratePorts ? 1 : 0)}");
            sb.AppendLine($"HexColumns={config.HexColumns}");

            try
            {
                File.WriteAllText(Path.GetFullPath(filePath), sb.ToString());
            }
            catch (Exception)
            {
                config.MsgLogger.LogError($"Can't save configuration to '{filePath}'");
                return false;
            }
            return true;
        }

        public static bool Load(this Configuration config, string filePath)
        {

            int ver = 0;

            try
            {
                var fullPath = Path.GetFullPath(filePath);
                if (!File.Exists(fullPath))
                {
                    config.MsgLogger.LogError($"Can't load configuration from '{filePath}'");
                    return false;
                }

                foreach(var line in File.ReadAllLines(fullPath).Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    // First line must be Ver
                    if (ver == 0)
                    {
                        if (!line.FindValueInt("Ver", out ver) || ver < 1)
                            return ReturnError(config.MsgLogger, "Invalid or missing load file version");
                    }
                    else
                        ParseConfig(config, line, ver);
                }
            }
            catch (Exception)
            {
                config.MsgLogger.LogError($"Can't load configuration from '{filePath}'");
                return false;
            }
            return true;
        }

        static IsolatedStorageFile GetStore(bool isUser)
        {
            IsolatedStorageScope scope = isUser
                ? IsolatedStorageScope.User
                : IsolatedStorageScope.Machine;
            return IsolatedStorageFile.GetStore(scope, null, null);
        }

        static void ParseConfig(this Configuration config, string line, int ver)
        {
            if (line.FindValue("COMPort", out string comPort)) config.COMPort = comPort;
            else if (line.FindValueInt("BaudRate", out int baud)) config.BaudRate = baud;
            else if (line.FindValueInt("DataBits", out int databits)) config.DataBits = databits;
            else if (line.FindValueEnum("StopBits", out StopBits stopbits)) config.StopBits = stopbits;
            else if (line.FindValueEnum("Parity", out Parity parity)) config.Parity = parity;
            else if (line.FindValueEnum("FlowControl", out Handshake flowcontrol)) config.FlowControl = flowcontrol;
            else if (line.FindValueInt("LogFileSize", out int logfilesize)) config.LogFileSize = logfilesize;
            else if (line.FindValue("LogPath", out string logPath)) config.LogPath = logPath;
            else if (line.FindValueBool("IsLogging", out bool islogging)) config.IsLogging = islogging;
            else if (line.FindValueBool("IsBinaryLogging", out bool isbinlogging)) config.IsBinaryLogging = isbinlogging;
            else if (line.FindValueBool("IgnoreEmptyLines", out bool ignoreempty)) config.IgnoreEmptyLines = ignoreempty;
            else if (line.FindValueBool("IsHexMode", out bool ishex)) config.IsHexMode = ishex;
            else if (line.FindValueBool("AllowKeyEntry", out bool allowkey)) config.AllowKeyEntry = allowkey;
            else if (line.FindValueBool("LineWrap", out bool linewrap)) config.LineWrap = linewrap;
            else if (line.FindValueBool("EnumeratePorts", out bool enumports)) config.EnumeratePorts = enumports;
            else if (line.FindValueInt("HexColumns", out int hexcols)) config.HexColumns = hexcols;
        }

        static bool ReturnError(IMessageLogger logger, string msg)
        {
            logger.LogError(msg);
            return false;
        }

        static bool FindValueEnum<TEnum>(this string line, string key, out TEnum value)
        {
            
            if (FindValueInt(line, key, out int iVal) &&
                Enum.IsDefined(typeof(TEnum), iVal))
            {
                value = (TEnum)(object)iVal;
                return true;
            }
            value = default;
            return false;
        }

        static bool FindValueBool(this string line, string key, out bool value)
        {
            if (FindValueInt(line, key, out int iVal))
            {
                value = iVal != 0;
                return true;
            }
            value = false;
            return false;
        }

        static bool FindValueInt(this string line, string key, out int value)
        {
            if (FindValue(line, key, out string sVal) &&
                int.TryParse(sVal, out int iVal) &&
                iVal >= 0)
            {
                value = iVal;
                return true;
            }
            value = 0;
            return false;
        }

        static bool FindValue(this string line, string key, out string value)
        {
            var keyEqual = $"{key}=";
            if (line.StartsWith(keyEqual))
            {
                value = line[keyEqual.Length..];
                return true;
            }
            value = string.Empty;
            return false;
        }
    }
    #endregion ConfigExtensions
}
