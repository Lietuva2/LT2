using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Data.ViewModels.Project
{
    public class MemberModel
    {
        public string ObjectId { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public string Email { get; set; }

        public override bool Equals(object obj)
        {
            var o = (MemberModel) obj;
            return this.ObjectId == o.ObjectId && this.Email == o.Email && this.FullName == o.FullName;
        }

        public override int GetHashCode()
        {
            return ObjectId.GetHashCode();
        }
    }
}
