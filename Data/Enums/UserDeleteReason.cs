using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Data.Enums
{
    public enum UserDeleteReason
    {
        NotInterested,
        SearchEngines,
        DifferentViews,
        Insulted,
        UnintendedRegistration,
        DuplicatedAccount,
        CannotConfirm,
        Other = 99
    }
}
