using Data.LinqToSQL.Reporting;
using Framework.Data.Sessions.LinqToSQL;

namespace Data.Infrastructure.Sessions {
    public class ReportingSession:LinqToSqlSession, IReporting {

        public ReportingSession() : base(new DB()) { }

    }
}
