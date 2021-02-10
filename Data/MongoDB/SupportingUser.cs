using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;

namespace Data.MongoDB
{
    public class SupportingUser
    {
        public string Id { get; set; }
        public string FullName { get; set; }
        public bool IsPublic { get; set; }
        public ForAgainst ForAgainst { get; set; }
        public DateTime? Date { get; set; }
        public string PersonCode { get; set; }
    }
}
