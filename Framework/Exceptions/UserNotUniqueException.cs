using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Framework.Exceptions
{
    public class UserNotUniqueException : Exception
    {
        public UserNotUniqueException()
            : base("User must be unique")
        {
        }
    }
}
