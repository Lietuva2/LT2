using System.Globalization;

#if LT2_CONTRACTS
namespace Lt2.Api.Contracts.Parliament;
#else
namespace Lt2.SharedKernel;
#endif

public static class VoteLinks
{
    public static string Page(long voteId) => "/seimas/balsavimai/" + voteId.ToString(CultureInfo.InvariantCulture);
}
