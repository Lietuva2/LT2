using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using Framework.Data.Sessions;
using Framework.Infrastructure.Logging;

namespace Framework.Infrastructure.Notification
{
    public class NotificationTaskBase : INotificationTask
    {
        /// <summary>
        /// Generic email sender.
        /// </summary>
        protected readonly IMailSender mailSender;

        protected readonly ISmsSender smsSender;

        protected readonly ILogger logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NotificationTaskBase"/> class.
        /// </summary>
        /// <param name="mailSender">The mail sender.</param>
        /// <param name="smsSender">The SMS sender.</param>
        public NotificationTaskBase(IMailSender mailSender, ISmsSender smsSender, ILogger logger)
        {
            this.mailSender = mailSender;
            this.smsSender = smsSender;
            this.logger = logger;
            SendEmail = true;
            SendSms = false;
        }

        public bool SendEmail { get; set; }
        public bool SendSms { get; set; }

        public string To { get; set; }
        public string From { get; set; }
        public string Number { get; set; }
        public string Subject { get; set; }
        public string FromDisplayName { get; set; }
        public string ToDisplayName { get; set; }
        public string Cc { get; set; }
        public bool IncludeUnsubscribe { get; set; }
        public string Sign { get; set; }

        public Exception Exception { get; set; }

        /// <summary>
        /// Gets the message.
        /// </summary>
        /// <value>The message.</value>
        public string MessageTemplate { get; set; }

        public string SmsTemplate { get; set; }

        /// <summary>
        /// Executes current task.
        /// </summary>
        public virtual MailMessage Execute(params string[] args)
        {
            if (SendEmail && MessageTemplate != null)
            {

                var notification = new MailMessage();
                notification.Subject = Subject; 
                
                notification.To.Add(new MailAddress(To, ToDisplayName));
                if(string.IsNullOrEmpty(this.From))
                {
                    this.From = "mail@lietuva2.net";
                }
                if(this.From == "mail@lietuva2.net")
                {
                    this.FromDisplayName = "Lietuva 2.0";
                }

                try
                {
                    notification.From = new MailAddress(From, FromDisplayName);
                    if (!string.IsNullOrEmpty(Cc))
                    {
                        notification.CC.Add(Cc);
                    }
                }
                catch(Exception e)
                {
                    logger.Warning("From adress create failed", e);
                    notification.From = new MailAddress("mail@lietuva2.net", From);
                }

                notification.Body = string.Format(MessageTemplate, args);
                if (IncludeUnsubscribe)
                {
                    notification.Body = AppendUnsubscribe(notification.Body);
                }

                try
                {
                    mailSender.Send(notification);
                    //logger.Information(string.Format("Mail sent to: {0}\nSubject: {1}\nBody: {2}", notification.To, notification.Subject, notification.Body));
                }
                catch(Exception e)
                {
                    logger.Warning("Send email failed", e);
                    if (e.InnerException != null)
                    {
                        Exception = e.InnerException;
                    }
                    else
                    {
                        Exception = e;
                    }
                }

                return notification;
            }

            return null;

            //if(SendSms && !string.IsNullOrEmpty(SmsTemplate))
            //{
            //    var notification = new SmsNotification();
            //    notification.ToAddress = Number;
            //    notification.Body = string.Format(SmsTemplate, args);
            //    try
            //    {
            //        smsSender.Send(notification);
            //    }
            //    catch (Exception e)
            //    {
            //        logger.Warning("Send SMS failed", e);
            //    }
            //}

        }

        private string AppendUnsubscribe(string body)
        {
            return body + string.Format("<div style='font-size:8.0pt;font-family:Verdana,sans-serif;color:#7f7f7f'>Nebenorite gauti laiškų? <a href='https://www.lietuva2.lt/Account/Unsubscribe?email={0}&sign={1}'>Atsisakyti</a></div>", To, Hashing.Hashing.GetMd5Hash(To + "LT2rulez"));
        }
    }
}
