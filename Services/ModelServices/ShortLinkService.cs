using Data.EF.Voting;
using Framework;
using System.Linq;
using Framework.Strings;

namespace Services.ModelServices
{
    public class ShortLinkService : IService
    {
        private IVotingContextFactory votingSessionFactory;

        public ShortLinkService(
            IVotingContextFactory votingSessionFactory)
        {
            this.votingSessionFactory = votingSessionFactory;
        }

        public string GetShortLink(string shortLink, string fullLink)
        {
            using (var session = votingSessionFactory.CreateContext(true))
            {
                shortLink = shortLink.ToSeoUrl().LimitLength(50, string.Empty);
                var dbLink = GetShortLinkQuery(shortLink, fullLink).SingleOrDefault();
                if (dbLink != null)
                {
                    return dbLink.Id;
                }
                var sl = shortLink;
                int count = 1;
                while (ShortLinkExists(sl))
                {
                    sl = shortLink + "-" + count++;
                    dbLink = GetShortLinkQuery(sl, fullLink).SingleOrDefault();
                    if (dbLink != null)
                    {
                        return dbLink.Id;
                    }
                }

                session.ShortLinks.Add(new ShortLink() {Id = sl, FullLink = fullLink});
                return sl;
            }
        }

        public bool ShortLinkExists(string shortLink)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                return GetShortLinkQuery(shortLink).Select(l => l.FullLink).Any();
            }
        }

        public string GetFullLink(string id)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                return GetShortLinkQuery(id).Select(l => l.FullLink).SingleOrDefault();
            }
        }

        private IQueryable<ShortLink> GetShortLinkQuery(string id, string fullLink = null)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                var urlDecoded = System.Web.HttpUtility.UrlDecode(id);

                var query = session.ShortLinks.Where(s => s.Id == id || s.Id == urlDecoded);

                if(!string.IsNullOrEmpty(fullLink))
                {
                    query = query.Where(l => l.FullLink == fullLink);
                }

                return query;
            }
        }
    }
}
