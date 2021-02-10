using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Infrastructure.Sessions;
using Data.LinqToSQL.Reporting.Entities;
using Services.Enums;
using Services.Session;

namespace Services.ModelServices
{
    public class ReportingService
    {
        private UserInfo CurrentUser { get { return MembershipSession.GetUser(); } }
        private Func<IReporting> reportingSessionFactory;

        public ReportingService(
            Func<IReporting> reportingSessionFactory)
        {
            this.reportingSessionFactory = reportingSessionFactory;
        }

        public void LogUserActivity(string activity, LogTypes typeId)
        {
            LogUserActivity(CurrentUser.UserName, activity, CurrentUser.Ip, typeId);
        }

        public void LogUserActivity(string userName, string activity, string ipAddress, LogTypes typeId)
        {
            var log = new UserActivity
            {
                Activity = activity,
                CreatedOn = DateTime.Now,
                UserName = userName,
                IpAddress = ipAddress,
                TypeId = (int)typeId
            };
            using (var reporting = reportingSessionFactory())
            {
                reporting.Add(log);
                reporting.CommitChanges();
            }
        }
    }
}
