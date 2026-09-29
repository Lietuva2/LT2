using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Framework.Infrastructure.Notification
{
    public interface INotification
    {
        string FromAddress { get; set; }
        string ToAddress { get; set; }
        string Body { get; set; }
    }
}
