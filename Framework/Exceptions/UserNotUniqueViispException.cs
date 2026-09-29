using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Framework.Exceptions
{
    public class UserNotUniqueViispException : Exception
    {
        public UserNotUniqueViispException()
            : base("User must be unique")
        {
        }
    }
}
