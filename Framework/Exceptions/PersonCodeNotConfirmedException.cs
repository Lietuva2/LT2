using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Framework.Exceptions
{
    public class PersonCodeNotConfirmedException : Exception
    {
        public PersonCodeNotConfirmedException()
            : base("Person code is not confirmed")
        {
        }
    }
}
