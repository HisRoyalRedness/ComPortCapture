using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace HisRoyalRedness.com
{
    public interface IMessageLogger
    {
        void LogInfo() => LogInfo(string.Empty);
        void LogInfo(string message);

        void LogWarning(string message);

        void LogError(string message) => LogError(null, message);
        void LogError(Exception ex, string message = null);
    }

    public class ConsoleLogger : IMessageLogger
    {
        public void LogInfo(string message)
        {
            Console.WriteLine(message);
        }

        public void LogWarning(string message)
        {
            Console.WriteLine($"WARNING: {message}");
        }

        public void LogError(Exception ex, string message)
        {
            if (ex == null)
                Console.WriteLine($"ERROR: {message}");
            else
                Console.WriteLine($"ERROR: {(string.IsNullOrEmpty(message) ? ex.Message : message)}{Environment.NewLine}{Environment.NewLine}{ex}");
        }
    }

    public class EventLogger : IMessageLogger
    {
        public EventLogger(ILogger logger)
        {
            _logger = logger;
        }

        public void LogInfo(string message)
        {
            _logger.LogInformation(message);
        }

        public void LogWarning(string message)
        {
            _logger.LogWarning(message);
        }

        public void LogError(Exception ex, string message)
        {
            if (ex == null)
                _logger.LogError(message);
            else
                _logger.LogError(ex, string.IsNullOrEmpty(message) ? ex.Message : message);
        }

        readonly ILogger _logger;
    }
}
