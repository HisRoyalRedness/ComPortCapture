using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace HisRoyalRedness.com
{
    using DataBuffer = BufferBlock<ReadOnlyMemory<byte>>;

    class Program
    {

        enum ParseState
        {
            NewFile,
            StartOfLine,
            LineData
        }

        [STAThread]
        static void Main(string[] args)
        {
            Console.TreatControlCAsInput = true;

            if (ParseCommandLine(args, out var config))
            {
                using (config.Logger = new LineLogger(config))
                {
                    var header =
                        $"COM port:       {config.COMPort}{Environment.NewLine}" +
                        $"Baud rate:      {config.BaudRate}{Environment.NewLine}" +
                        $"Data bits:      {config.DataBits}{Environment.NewLine}" +
                        $"Stop bits:      {config.StopBits}{Environment.NewLine}" +
                        $"Parity:         {config.Parity}{Environment.NewLine}" +
                        $"Flow control:   {config.FlowControl}{Environment.NewLine}" +
                        $"Ignore empty:   {config.IgnoreEmptyLines}{Environment.NewLine}";

                    if (config.IsBinaryLogging)
                        header += $"Binary log:     {config.BinLogPath}{Environment.NewLine}";

                    if (config.IsLogging)
                        header +=
                            $"Log path:       {config.LogPath}{Environment.NewLine}" +
                            $"Log file size:  {config.LogFileSize.ToFileSize()}{Environment.NewLine}";
                    else
                        header += $"Not logging to file{Environment.NewLine}";

                    if (config.IsLogging)
                        config.Logger.Header = header;
                    Console.WriteLine(header);

                    var dataQueue = new DataBuffer(); 
                    var cancelSource = new CancellationTokenSource();

                    var taskList = new List<Tuple<string, Task>>()
                    {
                        new Tuple<string, Task>( "Key handling", KeyHandlingAsync(config, cancelSource)),
                        new Tuple<string, Task>( "Serial read", SerialReadAsync(config, dataQueue, cancelSource.Token) ),
                        new Tuple<string, Task>( "Log write", LogWriteAsync(config, dataQueue, cancelSource.Token) )
                    };

                    var index = Task.WaitAny(taskList.Select(t => t.Item2).ToArray());
                    Console.WriteLine($"Exit: {taskList[index].Item1}");
                }
            }
            else
                Environment.ExitCode = 1;
        }

        #region Command line parsing
        static bool ParseCommandLine(string[] args, out Configuration config)
        {
            config = new Configuration();
            foreach (var arg in args)
            {
                var split = arg.Split('=');
                switch (split.Length)
                {
                    case 1:
                        // Com port
                        if (IsComPort(arg))
                            config.COMPort = arg.ToUpper();

                        // Baud rate
                        else if (int.TryParse(arg, out var baud))
                            config.BaudRate = baud;

                        // Config
                        else if (_configRegex.TryMatch(arg, out var match))
                        {
                            config.DataBits = int.Parse(match.Groups[1].Value);
                            if (match.Groups[2].Value.TryParse(out StopBits stopbits))
                                config.StopBits = stopbits;
                            if (match.Groups[3].Value.TryParse(out Parity parity))
                                config.Parity = parity;
                            if (match.Groups[4].Value.TryParse(out Handshake flow))
                                config.FlowControl = flow;
                        }

                        // Ignore empty lines
                        else if (string.Compare(arg, CMD_NOEMPTY, true) == 0)
                            config.IgnoreEmptyLines = true;

                        // Hex mode
                        else if (string.Compare(arg, CMD_HEXMODE, true) == 0)
                            config.IsHexMode = true;

                        // Anything else
                        else
                        {
                            Console.WriteLine($"ERROR: Unknown argument '{arg}'.");
                            return false;
                        }
                        break;

                    case 2:
                        switch (split[0].ToLower())
                        {
                            case CMD_COMPORT:
                                if (IsComPort(split[1]))
                                    config.COMPort = split[1].ToUpper();
                                else
                                {
                                    Console.WriteLine($"ERROR: Invalid COM port argument '{split[1]}'.");
                                    return false;
                                }
                                break;

                            case CMD_BAUD:
                                if (int.TryParse(split[1], out var baud))
                                    config.BaudRate = baud;
                                else
                                {
                                    Console.WriteLine($"ERROR: Invalid baud rate argument '{split[1]}'.");
                                    return false;
                                }
                                break;

                            case CMD_CONFIG:
                                if (_configRegex.TryMatch(split[1], out var match))
                                {
                                    config.DataBits = int.Parse(match.Groups[1].Value);
                                    if (match.Groups[2].Value.TryParse(out StopBits stopbits))
                                        config.StopBits = stopbits;
                                    if (match.Groups[3].Value.TryParse(out Parity parity))
                                        config.Parity = parity;
                                    if (match.Groups[4].Value.TryParse(out Handshake flow))
                                        config.FlowControl = flow;
                                }
                                else
                                {
                                    Console.WriteLine($"ERROR: Invalid config argument '{split[1]}'.");
                                    return false;
                                }
                                break;

                            case CMD_LOGPATH:
                                config.LogPath = Path.GetFullPath(split[1]);
                                if (!Directory.Exists(config.LogPath))
                                {
                                    try
                                    {
                                        Directory.CreateDirectory(config.LogPath);
                                    }
                                    catch(Exception ex)
                                    {
                                        Console.WriteLine(ex.Message);
                                        return false;
                                    }
                                }
                                config.IsLogging = true;
                                break;

                            case CMD_LOGSIZE:
                                if (split[1].TryParseFileSize(out long size))
                                    config.LogFileSize = size;
                                else
                                {
                                    Console.WriteLine($"ERROR: Invalid file size '{split[1]}'.");
                                    return false;
                                }
                                break;

                            case CMD_BINFILE:
                                config.BinLogPath = Path.GetFullPath(split[1]);
                                try
                                {
                                    File.WriteAllBytes(config.BinLogPath, new byte[] { });
                                    config.IsBinaryLogging = true;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(ex.Message);
                                    return false;
                                }
                                break;

                            case CMD_HEXMODE:
                                if (int.TryParse(split[1], out var hexCols) && hexCols >=8 && hexCols <= 64)
                                {
                                    config.IsHexMode = true;
                                    config.HexColumns = hexCols;
                                }
                                else
                                {
                                    Console.WriteLine($"ERROR: HexCols number '{split[1]}'. It must be between 8 and 64.");
                                    return false;
                                }
                                break;


                            default:
                                Console.WriteLine($"ERROR: Unknown argument '{split[0]}'.");
                                return false;
                        }
                        break;

                    default:

                        Console.WriteLine($"ERROR: Unknown argument '{arg}'.");
                        return false;
                }
            } //foreach

            if (string.IsNullOrEmpty(config.COMPort))
            {
                PrintUsage();
                return false;
            }

            return true;
        }

        static void PrintUsage()
        {
            var version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
            var fileName = Path.GetFileNameWithoutExtension(FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileName);

            const int alignment = -13;
            Console.WriteLine(
                $"{fileName}, version: {version}{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"   Usage: {fileName, -14} [{CMD_COMPORT}=]<comPort> [{CMD_BAUD}=<baudRate>] [{CMD_CONFIG}=<db,sb,pa,fl>] [{CMD_NOEMPTY}]{Environment.NewLine}" +
                $"                         [{CMD_LOGPATH}=<logFilePath>] [{CMD_LOGSIZE}=<maxLogSize>] [{CMD_BINFILE}=<binLogPath>]{Environment.NewLine}" +
                $"                         [{CMD_HEXMODE}[=<hexCols>]]{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"      where:{Environment.NewLine}" +
                $"         comPort:     The COM port to connect to, eg. COM1.{Environment.NewLine}" +
                $"         baudRate:    The baud rate. Default is {Configuration.DEFAULT_BAUD}.{Environment.NewLine}" +
                $"         config:      Comma-seperated port configuration. Default is {DEFAULT_CONFIG}.{Environment.NewLine}" +
                $"                        db = data bits (5-8){Environment.NewLine}" +
                $"                        sb = stop bits (0, 1, 1.5, 2){Environment.NewLine}" +
                $"                        pa = parity (n=none, o=odd, e=even, m=mark, s=space){Environment.NewLine}" +
                $"                        fl = flow control (n=none, r=rts/cts, x=xon/xoff, b=rts/cts and xon/xoff){Environment.NewLine}" +
                $"         {CMD_NOEMPTY + ":",alignment}Ignore empty lines.{Environment.NewLine}" +
                $"         logFilePath: The directory to log to. Logging disabled if omitted.{Environment.NewLine}" +
                $"         maxLogSize:  The size at which the log file rolls over, eg. 10KB, 1MB etc. Default is {Configuration.DEFAULT_FILE_SIZE.ToFileSize()}.{Environment.NewLine}" +
                $"         binLogPath:  The path to a file to log data in a binary format.{Environment.NewLine}" +
                $"         {CMD_HEXMODE + ":",alignment}Display data as hex. Optionally specify the number of columns. Default is {Configuration.DEFAULT_HEXCOLS}.{Environment.NewLine}" +
                $"{Environment.NewLine}"
            );
        }

        static bool IsComPort(string inp) => _comPortRegex.Match(inp).Success;

        const string CMD_COMPORT = "com";
        const string CMD_BAUD = "baud";
        const string CMD_CONFIG = "config";
        const string CMD_LOGPATH = "logpath";
        const string CMD_LOGSIZE = "logsize";
        const string CMD_BINFILE = "binfile";
        const string CMD_NOEMPTY = "noempty";
        const string CMD_HEXMODE = "hex";


        static readonly string DEFAULT_CONFIG = $"{Configuration.DEFAULT_DATA_BITS},{Configuration.DEFAULT_STOP_BITS.ToConfigString()},{Configuration.DEFAULT_PARITY.ToConfigString()},{Configuration.DEFAULT_FLOW_CONTROL.ToConfigString()}";

        static Regex _comPortRegex = new Regex(@"^COM\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static Regex _configRegex = new Regex(@"^([5-8])\s*,\s*(0|1|1.5|2)\s*,\s*([noems])\s*,\s*([nrxb])$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        #endregion Command line parsing

        #region Key handling
        static Task KeyHandlingAsync(Configuration config, CancellationTokenSource cancelSource)
            => Task.Run(() =>
            {
                Console.WriteLine("Hit 'Q', Esc or Ctrl-C to quit");
                if (config.IsLogging)
                    Console.WriteLine("Hit 'Space' to start logging to a new log file");
                Console.WriteLine();

                while (!cancelSource.IsCancellationRequested)
                {
                    var keyInfo = Console.ReadKey(true);
                    switch (keyInfo.Key)
                    {
                        case ConsoleKey.Q:
                        case ConsoleKey.Escape:
                            cancelSource.Cancel();
                            break;
                        case ConsoleKey.C:
                            if (keyInfo.Modifiers == ConsoleModifiers.Control)
                                cancelSource.Cancel();
                            break;
                        case ConsoleKey.Spacebar:
                            if (config.IsLogging)
                                config.Logger.RollLog();
                            break;
                    }
                }
            });
        #endregion Key handling

        #region Serial read
        static Task SerialReadAsync(Configuration config, DataBuffer dataQueue, CancellationToken cancelToken)
        {
            return Task.Run(async () =>
            {
                try
                {
                    using (var serial = new SerialWrapper(config))
                    {
                        // Try open the port. Only one try is necessary
                        if (!serial.Open())
                            return;

                        var isReadValid = true;
                        while (isReadValid && !cancelToken.IsCancellationRequested)
                        {
                            var retries = 10;
                            do
                            {
                                var result = await serial.ReadAsync(cancelToken);
                                isReadValid = result.IsReadValid;
                                if (isReadValid)
                                    dataQueue.Post(result.Buffer);

                                else
                                {
                                    Console.WriteLine($"{Environment.NewLine}WARNING: Cannot read from port {config.COMPort}. Retrying...");
                                    serial.Close();
                                    await Task.Delay(5000, cancelToken);

                                    // Try re-open the serial port, as many times as we can
                                    while (!serial.Open() && --retries > 0)
                                        await Task.Delay(2000, cancelToken);
                                }

                            } while (retries > 0 && !isReadValid);
                        }
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"{Environment.NewLine}{Environment.NewLine}ERROR: {ex.Message}{Environment.NewLine}{Environment.NewLine}{ex}");
                }
            });
        }
        #endregion Serial read

        #region Log write
        static Task LogWriteAsync(Configuration config, DataBuffer dataQueue, CancellationToken cancelToken)
        {
            return Task.Run(async () =>
            {
                try
                {
                    using (var binLog = config.IsBinaryLogging ? File.Open(config.BinLogPath, FileMode.Create, FileAccess.Write, FileShare.Read) : null)
                    {
                        while (!cancelToken.IsCancellationRequested)
                        {
                            var dataBlock = await dataQueue.ReceiveAsync(cancelToken);
                            if (dataBlock.Length > 0)
                            {
                                await config.Logger.WriteAsync(dataBlock, cancelToken);
                                if (config.IsBinaryLogging)
                                {
                                    await binLog.WriteAsync(dataBlock, cancelToken);
                                    await binLog.FlushAsync();
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{Environment.NewLine}{Environment.NewLine}ERROR: {ex.Message}{Environment.NewLine}{Environment.NewLine}{ex}");
                }
            });
        }
        #endregion Log write
    }
}
