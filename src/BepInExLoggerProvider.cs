using System;
using Microsoft.Extensions.Logging;
using BepLogLevel = BepInEx.Logging.LogLevel;

namespace TwitchIntegration
{
    internal class BepInExLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new BepInExLogger(categoryName);
        }

        public void Dispose() { }

        private class BepInExLogger : ILogger
        {
            private BepInEx.Logging.ManualLogSource logSource;

            public BepInExLogger(string categoryName)
            {
                logSource = new BepInEx.Logging.ManualLogSource(categoryName);
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                BepLogLevel level = logLevel switch
                {
                    LogLevel.Trace => BepLogLevel.Debug,
                    LogLevel.Debug => BepLogLevel.Debug,
                    LogLevel.Information => BepLogLevel.Info,
                    LogLevel.Warning => BepLogLevel.Warning,
                    LogLevel.Error => BepLogLevel.Error,
                    LogLevel.Critical => BepLogLevel.Fatal,
                    LogLevel.None => BepLogLevel.None,
                    _ => BepLogLevel.None
                };
                logSource.Log(level, formatter(state, exception));
            }
        }
    }
}
