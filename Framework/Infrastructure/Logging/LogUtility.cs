using System;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Framework.Infrastructure.Logging {
    public class LogUtility {
        public static string BuildExceptionMessage(Exception exception)
        {
            return BuildExceptionMessage(exception, string.Empty);
        }

        public static string BuildExceptionMessage(Exception x, string message) {
			
			Exception logException=x;
			if(x.InnerException!=null)
				logException=x.InnerException;

            if(x.GetType() == typeof(ReflectionTypeLoadException))
            {
                logException = (x as ReflectionTypeLoadException).LoaderExceptions[0];
            }

            string strErrorMsg = Environment.NewLine;

            if(!string.IsNullOrEmpty(message))
            {
                strErrorMsg += Environment.NewLine + "Custom Message :" + message;
            }
			
			// Get the error message
            strErrorMsg += Environment.NewLine + "Message :" + logException.Message;

			// Source of the message
            strErrorMsg += Environment.NewLine + "Source :" + logException.Source;

			// Stack Trace of the error

            strErrorMsg += Environment.NewLine + "Stack Trace :" + logException.StackTrace;

			// Method where the error occurred
            strErrorMsg += Environment.NewLine + "TargetSite :" + logException.TargetSite;
			return strErrorMsg;
        }
    }
}
