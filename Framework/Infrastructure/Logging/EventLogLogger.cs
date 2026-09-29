using System;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace Framework.Infrastructure.Logging {
    public class EventLogLogger:ILogger
    {
        private readonly string storeName;
        public EventLogLogger(string storeName)
        {
            this.storeName = storeName;
        }

        #region ILogger Members

        public void Info(string message) {
            WriteToEventLog(message, EventLogEntryType.Information);
        }

        public void Warn(string message) {
            WriteToEventLog(message, EventLogEntryType.Warning);
        }

        public void Debug(string message) {
            //no need to send this to the EventLog - output it 
            //to the DEBUG window
            System.Diagnostics.Debug.WriteLine(message);
        }

        public void Error(string message) {
            WriteToEventLog(message, EventLogEntryType.Error);
        }

        public void Error(Exception x) {
            Error(LogUtility.BuildExceptionMessage(x));
        }

        public void Fatal(string message) {
            WriteToEventLog(message, EventLogEntryType.Error);
        }
        public void Fatal(Exception x) {
            WriteToEventLog(LogUtility.BuildExceptionMessage(x), EventLogEntryType.Error);
        }

        public void Error(string message, Exception exception)
        {
            WriteToEventLog(LogUtility.BuildExceptionMessage(exception, message), EventLogEntryType.Error);
        }

        public void Warning(string message)
        {
            WriteToEventLog(message, EventLogEntryType.Warning);
        }

        public void Warning(string message, Exception exception)
        {
            WriteToEventLog(LogUtility.BuildExceptionMessage(exception, message), EventLogEntryType.Warning);
        }

        public void Warning(Exception exception)
        {
            WriteToEventLog(LogUtility.BuildExceptionMessage(exception), EventLogEntryType.Warning);
        }

        public void Information(string message)
        {
            WriteToEventLog(message, EventLogEntryType.Information);
        }

        public void Information(string message, Exception exception)
        {
            WriteToEventLog(LogUtility.BuildExceptionMessage(exception), EventLogEntryType.Information);
        }

        public void Information(Exception exception)
        {
            WriteToEventLog(LogUtility.BuildExceptionMessage(exception), EventLogEntryType.Information);
        }

        void WriteToEventLog(string message, EventLogEntryType logType)
        {

            EventLog eventLog = new EventLog();

            eventLog.Source = storeName;

            eventLog.WriteEntry(message, logType);
        }
        #endregion
    }
}
