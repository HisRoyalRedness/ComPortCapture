using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HisRoyalRedness.com
{
    using DataBuffer = ConcurrentCircularBuffer<byte>;

    class Capture
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
            //System.Diagnostics.Debugger.Launch();
            if (IsInteractive())
                RunAsConsole(args);
            else
                RunAsService(args);
        }

        static bool IsInteractive()
        {
            // Environment.UserInteractive doesn't seem to work, 
            // so use this hack to figure out if we're running as a service
            return Console.OpenStandardInput() != Stream.Null;
        }

        static void RunAsConsole(string[] args)
        {
            IMessageLogger logger = new ConsoleLogger();
            Console.TreatControlCAsInput = true;
            if (ParseCommandLine(args, logger, out var config))
            {
                string exitMsg = string.Empty;
                using (config.Logger = new LineLogger(config))
                {
                    var header = GenerateHeader(config);

                    if (config.IsLogging)
                        config.Logger.Header = header;
                    logger.LogInfo(header);

                    var dataQueue = new DataBuffer(BUFFER_SIZE * 10);
                    var cancelSource = new CancellationTokenSource();

                    using (var serial = new SerialWrapper(config, dataQueue, BUFFER_SIZE / 2))
                    {
                        try
                        {
                            var taskList = new List<Tuple<string, Task>>()
                            {
                                new Tuple<string, Task>( "Serial read", SerialReadAsync(config, dataQueue, cancelSource.Token, serial) ),
                                new Tuple<string, Task>( "Key handling", KeyHandlingAsync(config, cancelSource, serial)),
                                new Tuple<string, Task>( "Log write", LogWriteAsync(config, dataQueue, cancelSource.Token) )
                            };

                            // Wait for any of the tasks to end
                            var index = Task.WaitAny(taskList.Select(t => t.Item2).ToArray());

                            // Cancel the other tasks
                            cancelSource.Cancel();

                            // Wait for the logger to complete (it must be the last task!)
                            Task.WaitAll(taskList.Select(t => t.Item2).Last());
                            exitMsg = $"{Environment.NewLine}Exit: {taskList[index].Item1}";
                        }
                        catch (OperationCanceledException)
                        {
                            // Ignore cancellations
                        }
                        catch (Exception ex)
                        {
                            logger.LogInfo();
                            logger.LogInfo();
                            logger.LogError(ex);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(exitMsg))
                    logger.LogInfo(exitMsg);
            }
            else
                Environment.ExitCode = 1;
        }

        static void RunAsService(string[] args)
        {
            IHost host = Host.CreateDefaultBuilder(args)
                .UseWindowsService(options =>
                {
                    options.ServiceName = "ComPortCapture";
                })
                .ConfigureServices(services =>
                {
                    LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(services);
                    services.AddSingleton(new CaptureService(args.Length > 0 ? args[0] : string.Empty));
                    services.AddHostedService<WindowsBackgroundService>();
                })
                .ConfigureLogging((context, logging) =>
                {
                    // See: https://github.com/dotnet/runtime/issues/47303
                    logging.AddConfiguration(context.Configuration.GetSection("Logging"));
                })
                .Build();

            host.Run();
        }

        public static string GenerateHeader(Configuration config)
        {
            var header =
                $"COM port:       {config.COMPort}{Environment.NewLine}" +
                $"Baud rate:      {config.BaudRate}{Environment.NewLine}" +
                $"Data bits:      {config.DataBits}{Environment.NewLine}" +
                $"Stop bits:      {config.StopBits}{Environment.NewLine}" +
                $"Parity:         {config.Parity}{Environment.NewLine}" +
                $"Flow control:   {config.FlowControl}{Environment.NewLine}" +
                $"Ignore empty:   {config.IgnoreEmptyLines}{Environment.NewLine}" +
                $"Line wrap:      {config.LineWrap}{Environment.NewLine}" +
                $"Allow keypress: {config.AllowKeyEntry}{Environment.NewLine}";

            if (config.IsBinaryLogging)
                header += $"Binary log:     {config.BinLogPath}{Environment.NewLine}";

            if (config.IsLogging)
                header +=
                    $"Log path:       {config.LogPath}{Environment.NewLine}" +
                    $"Log file size:  {config.LogFileSize.ToFileSize()}{Environment.NewLine}";
            else
                header += $"Not logging to file{Environment.NewLine}";

            return header;
        }

        #region Command line parsing
        static bool ParseCommandLine(string[] args, IMessageLogger logger, out Configuration config)
        {
            config = new Configuration()
            {
                MsgLogger = logger
            };
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
                        else if (IsSwitchPresent(arg, CMD_NOEMPTY))
                            config.IgnoreEmptyLines = true;

                        // Hex mode
                        else if (IsSwitchPresent(arg, CMD_HEXMODE))
                            config.IsHexMode = true;

                        // Key entry
                        else if (IsSwitchPresent(arg, CMD_KEYENTRY))
                            config.AllowKeyEntry = true;

                        // Line wrap
                        else if (IsSwitchPresent(arg, CMD_WRAP))
                            config.LineWrap = true;

                        // Enumerate ports
                        else if (IsSwitchPresent(arg, CMD_ENUM_PORTS))
                            config.EnumeratePorts = true;

                        // Anything else
                        else
                        {
                            logger.LogError($"Unknown argument '{arg}'.");
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
                                    logger.LogError($"Invalid COM port argument '{split[1]}'.");
                                    return false;
                                }
                                break;

                            case CMD_BAUD:
                                if (int.TryParse(split[1], out var baud))
                                    config.BaudRate = baud;
                                else
                                {
                                    logger.LogError($"Invalid baud rate argument '{split[1]}'.");
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
                                    logger.LogError($"Invalid config argument '{split[1]}'.");
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
                                        logger.LogError(ex);
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
                                    logger.LogError($"Invalid file size '{split[1]}'.");
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
                                    logger.LogError(ex);
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
                                    logger.LogError($"HexCols number '{split[1]}'. It must be between 8 and 64.");
                                    return false;
                                }
                                break;

                            case CMD_SAVE:
                                config.SaveName = split[1];
                                break;

                            case CMD_LOAD:
                                config.LoadName = split[1];
                                if (!config.Load(config.LoadName))
                                    return false;
                                break;

                            default:
                                logger.LogError($"Unknown argument '{split[0]}'.");
                                return false;
                        }
                        break;

                    default:

                        logger.LogError($"Unknown argument '{arg}'.");
                        return false;
                }
            } //foreach

            if (!string.IsNullOrWhiteSpace(config.SaveName) &&
                !config.Save(config.SaveName))
                return false;

            if (config.EnumeratePorts)
            {
                var ports = SerialPort
                    .GetPortNames()
                    .OrderBy(p => int.TryParse(
                        p.Replace("COM", string.Empty, StringComparison.CurrentCultureIgnoreCase),
                        out int portInt)
                    ? portInt
                    : -1);
    
                logger.LogInfo($"Available ports: {string.Join(", ", ports)}{Environment.NewLine}");
            }

            if (string.IsNullOrEmpty(config.COMPort))
            {
                PrintUsage(logger);
                return false;
            }

            return true;
        }

        static void PrintUsage(IMessageLogger logger)
        {
            var filePath = Process.GetCurrentProcess().MainModule.FileName;
            var version = FileVersionInfo.GetVersionInfo(filePath).FileVersion;
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            const int alignment = -13;
            logger.LogInfo(
                $"{fileName}, version: {version}{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"   Usage: {fileName,-14} [{CMD_COMPORT}=]<comPort> [{CMD_BAUD}=<baudRate>] [{CMD_CONFIG}=<db,sb,pa,fl>] [{CMD_NOEMPTY}]{Environment.NewLine}" +
                $"                         [{CMD_LOGPATH}=<logFilePath>] [{CMD_LOGSIZE}=<maxLogSize>] [{CMD_BINFILE}=<binLogPath>]{Environment.NewLine}" +
                $"                         [{CMD_HEXMODE}[=<hexCols>]] [{CMD_KEYENTRY}] [{CMD_WRAP}] [{CMD_ENUM_PORTS}] [{CMD_SAVE}=<name>] [{CMD_LOAD}=<name>]{Environment.NewLine}" +
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
                $"         {CMD_KEYENTRY + ":",alignment}All simple keyboard entry to be sent over the serial port.{Environment.NewLine}" +
                $"         {CMD_WRAP + ":",alignment}Wrap the line if it's longer than the console window.{Environment.NewLine}" +
                $"         {CMD_SAVE + ":",alignment}Save the configuration as <name>.{Environment.NewLine}" +
                $"         {CMD_LOAD + ":",alignment}Load the configuration from <name>.{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"To run as a service, using this command:{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"       sc create \"<service name>\" binpath=\"<path to ComPortCapture.exe> <path to config file>\"{Environment.NewLine}" +
                $"{Environment.NewLine}"

            ); ;
        }

        static bool IsComPort(string inp) => _comPortRegex.Match(inp).Success;
        static bool IsSwitchPresent(string arg, string swtch) => string.Compare(arg, swtch, true) == 0;

        const string CMD_COMPORT    = "com";
        const string CMD_BAUD       = "baud";
        const string CMD_CONFIG     = "config";
        const string CMD_LOGPATH    = "logpath";
        const string CMD_LOGSIZE    = "logsize";
        const string CMD_BINFILE    = "binfile";
        const string CMD_NOEMPTY    = "noempty";
        const string CMD_HEXMODE    = "hex";
        const string CMD_KEYENTRY   = "key";
        const string CMD_WRAP       = "wrap";
        const string CMD_ENUM_PORTS = "ports";
        const string CMD_SAVE       = "save";
        const string CMD_LOAD       = "load";


        static readonly string DEFAULT_CONFIG = $"{Configuration.DEFAULT_DATA_BITS},{Configuration.DEFAULT_STOP_BITS.ToConfigString()},{Configuration.DEFAULT_PARITY.ToConfigString()},{Configuration.DEFAULT_FLOW_CONTROL.ToConfigString()}";

        static Regex _comPortRegex = new Regex(@"^COM\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static Regex _configRegex = new Regex(@"^([5-8])\s*,\s*(0|1|1.5|2)\s*,\s*([noems])\s*,\s*([nrxb])$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        #endregion Command line parsing

        #region Key handling
        static Task KeyHandlingAsync(Configuration config, CancellationTokenSource cancelSource, SerialWrapper serial)
            => Task.Run(() =>
            {
                Console.WriteLine("Ctrl-C to quit");
                Console.WriteLine("Ctrl-X to clear screen");
                if (config.IsLogging)
                    Console.WriteLine("Ctrl-Space to start logging to a new log file");
                Console.WriteLine();

                while (!cancelSource.IsCancellationRequested)
                {
                    var keyInfo = Console.ReadKey(true);
                    // Application control
                    if (keyInfo.Modifiers == ConsoleModifiers.Control)
                    {
                        switch (keyInfo.Key)
                        {
                            case ConsoleKey.C:
                                cancelSource.Cancel();
                                break;
                            case ConsoleKey.X:
                                Console.Clear();
                                Console.WriteLine(GenerateHeader(config));
                                Console.WriteLine("Ctrl-C to quit");
                                Console.WriteLine("Ctrl-X to clear screen");
                                if (config.IsLogging)
                                    Console.WriteLine("Ctrl-Space to start logging to a new log file");
                                Console.WriteLine();
                                break;
                            case ConsoleKey.Spacebar:
                                if (config.IsLogging)
                                    config.Logger.RollLog();
                                break;
                        }
                    }
                    // Text input
                    else if (config.AllowKeyEntry)
                    {
                        var data = Encoding.ASCII.GetBytes(new[] { keyInfo.KeyChar });
                        serial.WriteAsync(data, 0, data.Length);
                    }
                }
            });
        #endregion Key handling

        #region Serial read
        public static Task SerialReadAsync(Configuration config, DataBuffer dataQueue, CancellationToken cancelToken, SerialWrapper serial)
        {
            return Task.Run(async () =>
            {
                try
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
                            var bytesRead = await serial.ReadAsync(cancelToken);
                            if (bytesRead == 0)
                            {
                                // ReadAsync terminated because we cancelled a task.
                                // Just jump out
                                if (cancelToken.IsCancellationRequested)
                                    break;

                                config.MsgLogger.LogInfo();
                                config.MsgLogger.LogWarning($"{Environment.NewLine}Cannot read from port {config.COMPort}. Retrying...");
                                serial.Close();
                                await Task.Delay(5000, cancelToken);

                                // Try re-open the serial port, as many times as we can
                                while (!serial.Open() && --retries > 0)
                                    await Task.Delay(2000, cancelToken);
                            }

                        } while (retries > 0 && !isReadValid);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellations
                }
                catch (Exception ex)
                {
                    config.MsgLogger.LogInfo();
                    config.MsgLogger.LogInfo();
                    config.MsgLogger.LogError(ex);
                }
            });
        }
        #endregion Serial read

        #region Log write
        public static Task LogWriteAsync(Configuration config, DataBuffer dataQueue, CancellationToken cancelToken)
        {
            return Task.Run(async () =>
            {
                var buffer = new byte[BUFFER_SIZE];
                try
                {
                    using (var binLog = config.IsBinaryLogging ? File.Open(config.BinLogPath, FileMode.Create, FileAccess.Write, FileShare.Read) : null)
                    {
                        while (!cancelToken.IsCancellationRequested)
                        {
                            var bytesRead = await dataQueue.BlockedReadAsync(buffer, 0, buffer.Length, cancelToken);
                            if (bytesRead > 0)
                            {
                                await config.Logger.WriteAsync(buffer, 0, bytesRead, cancelToken);
                                if (config.IsBinaryLogging)
                                {
                                    await binLog.WriteAsync(buffer, 0, bytesRead, cancelToken);
                                    await binLog.FlushAsync(cancelToken);
                                }
                            }
                        }
                    }
                }
                catch(OperationCanceledException)
                {
                    // Ignore cancellations
                }
                catch (Exception ex)
                {
                    config.MsgLogger.LogInfo();
                    config.MsgLogger.LogInfo();
                    config.MsgLogger.LogError(ex);
                }
            });
        }
        #endregion Log write

        const int BUFFER_SIZE = 1024;
    }
}
