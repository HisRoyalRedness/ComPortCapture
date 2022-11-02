using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.ServiceProcess;

namespace HisRoyalRedness.com
{
    public sealed class WindowsBackgroundService : BackgroundService
    {
        public WindowsBackgroundService(
            CaptureService captureService,
            ILogger<WindowsBackgroundService> logger) =>
            (_captureService, _logger) = (captureService, logger);


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _captureService.StartAsync(_logger, stoppingToken);
                await StopAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Do nothing, the task was cancelled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);

                // Terminates this process and returns an exit code to the operating system.
                // This is required to avoid the 'BackgroundServiceExceptionBehavior', which
                // performs one of two scenarios:
                // 1. When set to "Ignore": will do nothing at all, errors cause zombie services.
                // 2. When set to "StopHost": will cleanly stop the host, and log errors.
                //
                // In order for the Windows Service Management system to leverage configured
                // recovery options, we need to terminate the process with a non-zero exit code.
                Environment.Exit(1);
            }
        }

        readonly CaptureService _captureService;
        readonly ILogger<WindowsBackgroundService> _logger;
    }
}
