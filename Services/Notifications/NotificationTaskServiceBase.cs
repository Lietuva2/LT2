using System;
using System.Collections.Generic;
using System.Linq;
using Data.EF.Actions;
using Data.EF.Users;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Notification;

namespace Services.Notifications
{
    /// <summary>
    /// Represents new order mailing task.
    /// </summary>
    public class NotificationTaskServiceBase : NotificationTaskBase
    {
        protected readonly IActionsContextFactory actionsContextFactory;
        protected readonly IUsersContextFactory usersContextFactory;
        public NotificationTaskServiceBase(IMailSender mailSender, ILogger logger, IActionsContextFactory actionsContextFactory, IUsersContextFactory usersContextFactory)
            : base(mailSender, null, logger)
        {
            this.actionsContextFactory = actionsContextFactory;
            this.usersContextFactory = usersContextFactory;
        }

        public new bool Execute(params string[] args)
        {
            SendEmail = true;
            SendSms = false;
            var msg = base.Execute(args);
            string userFromId;
            var emailsTo = msg.To.Select(m => m.Address).ToList();
            using (var usersContext = usersContextFactory.CreateContext())
            {
                var usersTo = (from ue in usersContext.UserEmails
                               where emailsTo.Contains(ue.Email)
                               select new {ue.User.ObjectId, ue.Email}).ToList();

                userFromId =
                    usersContext.UserEmails.Where(u => u.Email == msg.From.Address)
                           .Select(u => u.User.ObjectId)
                           .SingleOrDefault();

                using (var context = actionsContextFactory.CreateContext(true))
                {
                    var message = new Data.EF.Actions.Message()
                        {
                            Date = DateTime.Now,
                            Error = Exception != null ? Exception.Message : null,
                            From = msg.From.Address,
                            FromDisplayName = msg.From.DisplayName,
                            FromUserObjectId = userFromId,
                            Body = msg.Body,
                            Subject = msg.Subject
                        };
                    foreach (var to in msg.To)
                    {
                        message.To = to.Address;
                        message.ToDisplayName = to.DisplayName;
                        message.ToUserObjectId =
                            usersTo.Where(u => u.Email == to.Address).Select(u => u.ObjectId).SingleOrDefault();
                        context.Messages.Add(message);
                    }

                }
            }

            var success = Exception == null;
            Exception = null;
            return success;
        }
    }
}
