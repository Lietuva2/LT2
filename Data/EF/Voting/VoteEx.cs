using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Framework.Hashing;

namespace Data.EF.Voting
{
    public partial class Vote
    {
        public void Sign(string subject, string text)
        {
            Signature = DigitalSignature.Sign(GetSignValues(subject, text));
        }

        public bool ValidateSignature(string subject, string text)
        {
            return DigitalSignature.VerifySignature(Signature, GetSignValues(subject, text));
        }

        private string[] GetSignValues(string subject, string text)
        {
            return new[]
                       {
                           subject,
                           text,
                           FirstName,
                           LastName,
                           PersonCode,
                           ForAgainst.ToString(),
                           Date.ToString("yyyy-MM-dd HH:mm:ss"),
                           Source
                       };
        }
    }
}
