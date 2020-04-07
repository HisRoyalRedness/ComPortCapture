using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HisRoyalRedness.com
{
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
            #region Embedded resources
            //var assemblies = new System.Collections.Generic.Dictionary<string, System.Reflection.Assembly>();
            //var executingAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            //var resources = executingAssembly.GetManifestResourceNames().Where(n => n.EndsWith(".dll"));

            //// Fetch assemblies from our resource stream
            //foreach (string resource in resources)
            //{
            //    using (var stream = executingAssembly.GetManifestResourceStream(resource))
            //    {
            //        if (stream == null)
            //            continue;

            //        var bytes = new byte[stream.Length];
            //        stream.Read(bytes, 0, bytes.Length);
            //        try
            //        {
            //            assemblies.Add(resource, System.Reflection.Assembly.Load(bytes));
            //            System.Diagnostics.Debug.Print("Fetched {0} from resources.", resource);
            //        }
            //        catch (Exception ex)
            //        {
            //            System.Diagnostics.Debug.Print(string.Format("Failed to load: {0}, Exception: {1}", resource, ex.Message));
            //        }
            //    }
            //}

            //AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            //{
            //    // First, try find the assembly for an exact match (probably ignoring namespace)
            //    var assemblyName = new System.Reflection.AssemblyName(e.Name);
            //    var path = string.Format("{0}.dll", assemblyName.Name);
            //    if (assemblies.ContainsKey(path))
            //    {
            //        System.Diagnostics.Debug.Print("Found {0} in our assembly cache.", path);
            //        return assemblies[path];
            //    }
            //    else
            //    {
            //        // Not found? Now try with a fuzzy match (probably accounting for namespace)
            //        System.Diagnostics.Debug.Print("First chance not finding {0}.", path);
            //        var candidate = assemblies.Keys.Where(k => k.EndsWith(path, StringComparison.CurrentCultureIgnoreCase)).FirstOrDefault();
            //        if (candidate != null)
            //        {
            //            System.Diagnostics.Debug.Print("Found {0} in our assembly cache (second chance).", path);
            //            return assemblies[path];
            //        }
            //        else
            //        {
            //            System.Diagnostics.Debug.Print("Could not find {0}.", path);
            //            return null;
            //        }
            //    }
            //};
            #endregion Embedded resources

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
                        $"Flow control:   {config.FlowControl}{Environment.NewLine}";

                    if (config.IsLogging)
                        header +=
                            $"Log path:       {config.LogPath}{Environment.NewLine}" +
                            $"Log file size:  {config.LogFileSize.ToFileSize()}{Environment.NewLine}{Environment.NewLine}";
                    else
                        header += $"Not logging to file{Environment.NewLine}{Environment.NewLine}";

                    if (config.IsLogging)
                        config.Logger.Header = header;
                    Console.WriteLine(header);

                    var cancelSource = new CancellationTokenSource();
                    Task.WhenAny(
                        KeyTask(config, cancelSource),
                        new Program().Run(config, cancelSource.Token)
                    ).Wait();
                }
            }
            else
                Environment.ExitCode = 1;
        }

        static Task KeyTask(Configuration config, CancellationTokenSource cancelSource)
            => Task.Run(() =>
            {
                Console.WriteLine();
                Console.WriteLine("Hit 'Q' to quit");
                if (config.IsLogging)
                    Console.WriteLine("Hit 'Space' to start logging to a new log file");

                while (!cancelSource.IsCancellationRequested)
                {
                    switch (Console.ReadKey(true).Key)
                    {
                        case ConsoleKey.Q:
                            cancelSource.Cancel();
                            break;
                        case ConsoleKey.Spacebar:
                            if (config.IsLogging)
                                config.Logger.RollLog();
                            break;
                    }
                }
            });

        async Task Run(Configuration config, CancellationToken token)
        {
            await Task.Run(async () =>
            {
                using (var serial = new SerialWrapper(config))
                {
                    // Try open the port. Only one try is necessary
                    try
                    {
                        serial.Open();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("ERROR: Cannot open {0}. {1}", config.COMPort, ex.Message);
                        return;
                    }

                    var buffer = new char[1024];
                    var run = true;

                    while (run)
                    {
                        var retries = 10;
                        var bytesRead = 0;

                        do
                        {
                            bytesRead = serial.Read(buffer, 0, buffer.Length);
                            if (bytesRead <= 0)
                            {
                                --retries;
                                config.Logger.WriteWarning($"Cannot read from port {config.COMPort}. Retrying...");
                                serial.Close();
                                await Task.Delay(5000, token);

                                try
                                {
                                    serial.Open();
                                }
                                catch(Exception ex)
                                {
                                    if (retries <= 0)
                                    {
                                        config.Logger.WriteError($"Cannot open {config.COMPort}. {ex.Message}");
                                        run = false;
                                    }
                                }
                            }

                        } while (retries > 0 && bytesRead <= 0);

                        if (bytesRead > 0)
                            config.Logger.Write(buffer, 0, bytesRead);
                    }
                }
            }, token);
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

                        // Anything else
                        else
                        {
                            Console.WriteLine("ERROR: Unknown argument '{0}'.", arg);
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
                                    Console.WriteLine("ERROR: Invalid COM port argument '{0}'.", split[1]);
                                    return false;
                                }
                                break;

                            case CMD_BAUD:
                                if (int.TryParse(split[1], out var baud))
                                    config.BaudRate = baud;
                                else
                                {
                                    Console.WriteLine("ERROR: Invalid baud rate argument '{0}'.", split[1]);
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
                                    Console.WriteLine("ERROR: Invalid config argument '{0}'.", split[1]);
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
                                    Console.WriteLine("ERROR: Invalid file size '{0}'.", split[1]);
                                    return false;
                                }
                                break;

                            default:
                                Console.WriteLine("ERROR: Unknown argument '{0}'.", split[0]);
                                return false;
                        }
                        break;

                    default:

                        Console.WriteLine("ERROR: Unknown argument '{0}'.", arg);
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
            Console.WriteLine(
                $"   Usage: ComPortCapture [{CMD_COMPORT}=]<comPort> [{CMD_BAUD}=<baudRate>] [{CMD_CONFIG}=<db,sb,pa,fl>] [{CMD_LOGPATH}=<logFilePath>] [{CMD_LOGSIZE}=<maxLogSize>]{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"      where:{Environment.NewLine}" +
                $"         comPort:     The COM port to connect to, eg. COM1.{Environment.NewLine}" +
                $"         baudRate:    The baud rate. Default is {Configuration.DEFAULT_BAUD}.{Environment.NewLine}" +
                $"         config:      Comma-seperated port configuration. Default is {DEFAULT_CONFIG}.{Environment.NewLine}" +
                $"                        db = data bits (5-8){Environment.NewLine}" +
                $"                        sb = stop bits (0, 1, 1.5, 2){Environment.NewLine}" +
                $"                        pa = parity (n=none, o=odd, e=even, m=mark, s=space){Environment.NewLine}" +
                $"                        fl = flow control (n=none, r=rts/cts, x=xon/xoff, b=rts/cts and xon/xoff){Environment.NewLine}" +
                $"         logFilePath: The directory to log to. Logging disabled if omitted.{Environment.NewLine}" +
                $"         maxLogSize:  The size at which the log file rolls over, eg. 10KB, 1MB etc. Default is {Configuration.DEFAULT_FILE_SIZE.ToFileSize()}.{Environment.NewLine}" +
                $"{Environment.NewLine}"
            );
        }

        static bool IsComPort(string inp) => _comPortRegex.Match(inp).Success;

        const string CMD_COMPORT = "com";
        const string CMD_BAUD = "baud";
        const string CMD_CONFIG = "config";
        const string CMD_LOGPATH = "logpath";
        const string CMD_LOGSIZE = "logsize";

        static readonly string DEFAULT_CONFIG = $"{Configuration.DEFAULT_DATA_BITS},{Configuration.DEFAULT_STOP_BITS.ToConfigString()},{Configuration.DEFAULT_PARITY.ToConfigString()},{Configuration.DEFAULT_FLOW_CONTROL.ToConfigString()}";

        static Regex _comPortRegex = new Regex(@"^COM\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static Regex _configRegex = new Regex(@"^([5-8])\s*,\s*(0|1|1.5|2)\s*,\s*([noems])\s*,\s*([nrxb])$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        #endregion Command line parsing
    }
}
