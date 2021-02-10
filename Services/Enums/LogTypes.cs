using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Services.Enums
{
    public enum LogTypes
    {
        LogInOrOut = 0,
        BankLink = 1,
        UserCredentialsUpdated = 2,
        PasswordReset = 3,
        UserNameChanged = 4
    }
}
