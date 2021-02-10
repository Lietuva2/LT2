using System;
using System.Configuration;
using System.IO;
using Framework.Infrastructure.Logging;
using log4net;
using log4net.Config;

namespace Framework.Infrastructure.Logging
{
    /// <summary>
    /// Log4Net logger.
    /// </summary>
    public class Log4NetLogger : ILogger
    {
        /// <summary>
        /// The name of the key to get the log configuration file.
        /// </summary>
        private const string ConfigFileName = "log4net.config";

        /// <summary>
        /// The default name of the log.
        /// </summary>
        private const string LogName = "General";

        /// <summary>
        /// Currently configured log.
        /// </summary>
        private readonly ILog log;

        /// <summary>
        /// Initializes a new instance of the <see cref="Log4NetLogger"/> class.
        /// </summary>
        public Log4NetLogger()
            : this(LogName)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Log4NetLogger"/> class.
        /// </summary>
        /// <param name="logName">Name of the log.</param>
        public Log4NetLogger(string logName)
        {
            var configFileName = ConfigurationManager.AppSettings[ConfigFileName] ?? ConfigFileName;
            var logConfigFile = new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configFileName));
            if (logConfigFile.Exists)
            {
                XmlConfigurator.ConfigureAndWatch(logConfigFile);
            }
            else
            {
                XmlConfigurator.Configure();
            }

            log = LogManager.GetLogger(logName);
        }

        /// <summary>
        /// Configures logger.
        /// </summary>
        /// <returns></returns>
        public static Log4NetLogger Configure()
        {
            return new Log4NetLogger();
        }

        /// <summary>
        /// Configures logger.
        /// </summary>
        /// <param name="logName">Name of the log.</param>
        /// <returns></returns>
        public static Log4NetLogger Configure(string logName)
        {
            return new Log4NetLogger(logName);
        }

        public void Error(string message)
        {
            log.Error(message);
        }

        public void Error(string message, Exception exception)
        {
            log.Error(LogUtility.BuildExceptionMessage(exception, message));
        }

        public void Error(Exception exception)
        {
            log.Error(LogUtility.BuildExceptionMessage(exception));
        }

        public void Warning(string message)
        {
            log.Warn(message);
        }

        public void Warning(string message, Exception exception)
        {
            log.Warn(LogUtility.BuildExceptionMessage(exception, message));
        }

        public void Warning(Exception exception)
        {
            log.Warn(LogUtility.BuildExceptionMessage(exception));
        }

        public void Information(string message)
        {
            log.Info(message);
        }

        public void Information(string message, Exception exception)
        {
            log.Info(LogUtility.BuildExceptionMessage(exception, message));
        }

        public void Information(Exception exception)
        {
            log.Info(LogUtility.BuildExceptionMessage(exception));
        }
    }
}