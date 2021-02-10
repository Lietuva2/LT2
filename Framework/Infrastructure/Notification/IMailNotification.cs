using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Framework.Infrastructure.Notification
{
    public interface IMailNotification : INotification
    {
        string Subject { get; set; }
    }
}
