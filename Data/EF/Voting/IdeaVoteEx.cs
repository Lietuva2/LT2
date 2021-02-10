using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Framework.Hashing;

namespace Data.EF.Voting
{
    public partial class IdeaVote
    {
        public void Sign(string text)
        {
            Signature = DigitalSignature.Sign(GetSignValues(text));
        }

        private string[] GetSignValues(string text)
        {
            return new[]
                       {
                           text,
                           FirstName,
                           LastName,
                           PersonCode,
                           DocumentNo,
                           AddressLine,
                           Date.ToString("yyyy-MM-dd HH:mm:ss"),
                           Source
                       };
        }
    }
}
