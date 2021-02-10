using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Framework.Exceptions
{
    public class UserCannotSignException : Exception
    {
        public UserCannotSignException()
            : base("The current user cannot sign")
        {
        }
    }
}
