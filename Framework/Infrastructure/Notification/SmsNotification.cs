using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;

namespace Framework.Infrastructure.Notification
{
    public class SmsNotification : ISmsNotification
    {
        public string FromAddress { get; set; }
        public string ToAddress { get; set; }
        public string Body { get; set; }
    }
}
