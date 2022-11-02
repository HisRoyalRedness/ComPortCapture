using System;
using System.Collections.Generic;
using System.IO.IsolatedStorage;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace HisRoyalRedness.com
{
    using DataBuffer = ConcurrentCircularBuffer<byte>;

    public class CaptureService
    {
        public CaptureService(string loadFile)
        {
            LoadFile = loadFile;
        }

        public async Task<bool> StartAsync(ILogger logger, CancellationToken token)
        {
            MsgLogger = new EventLogger(logger);

             if (!File.Exists(LoadFile))
            {
                MsgLogger.LogError("Couldn't find the configuration file");
                return false;
            }

            var config = new Configuration()
            {
                MsgLogger = MsgLogger
            };
            
            if (!config.Load(LoadFile))
            {
                MsgLogger.LogError("Couldn't load the configuration file");
                return false;
            }

            if (!config.IsLogging && !config.IsBinaryLogging)
            {
                MsgLogger.LogError("Logging is not enabled");
                return false;
            }

            return await ListenAsync(config, token);
        }

        async Task<bool> ListenAsync(Configuration config, CancellationToken token)
        {
            using (config.Logger = new LineLogger(config))
            {
                var header = Capture.GenerateHeader(config);

                if (config.IsLogging)
                    config.Logger.Header = header;
                MsgLogger.LogWarning(header);

                var dataQueue = new DataBuffer(BUFFER_SIZE * 10);

                var cancelSource = CancellationTokenSource.CreateLinkedTokenSource(token);

                using (var serial = new SerialWrapper(config, dataQueue, BUFFER_SIZE / 2))
                {
                    try
                    {
                        var taskList = new List<Tuple<string, Task>>()
                            {
                                new Tuple<string, Task>( "Serial read", Capture.SerialReadAsync(config, dataQueue, cancelSource.Token, serial) ),
                                new Tuple<string, Task>( "Log write", Capture.LogWriteAsync(config, dataQueue, cancelSource.Token) )
                            };

                        // Wait for any of the tasks to end
                        await Task.WhenAny(taskList.Select(t => t.Item2));

                        // Cancel the other tasks
                        cancelSource.Cancel();

                        // Wait for the logger to complete (it must be the last task!)
                        await Task.WhenAll(taskList.Select(t => t.Item2).Last());

                        MsgLogger.LogWarning("Stopped monitoring: Shutting down");
                    }
                    catch (OperationCanceledException)
                    {
                        // Ignore cancellations
                    }
                    catch (Exception ex)
                    {
                        MsgLogger.LogInfo();
                        MsgLogger.LogInfo();
                        MsgLogger.LogError(ex);
                        return false;
                    }
                }
            }
            return true;
        }

        const int BUFFER_SIZE = 1024;

        public string LoadFile { get;  }
        public IMessageLogger MsgLogger { get; private set; }
    }
}
