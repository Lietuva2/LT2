using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;

namespace Data.MongoDB
{
    public class UserDetailsVisibility
    {
        public UserVisibility UserName { get; set; }
        public UserVisibility Email { get; set; }
        public UserVisibility EmploymentStatus { get; set; }
        public UserVisibility Address { get; set; }
        public UserVisibility OriginAddress { get; set; }
        public UserVisibility PostCode { get; set; }
        public UserVisibility Positions { get; set; }
        public UserVisibility Educations { get; set; }
        public UserVisibility WebSites { get; set; }
        public UserVisibility Interests { get; set; }
        public UserVisibility Groups { get; set; }
        public UserVisibility Awards { get; set; }
        public UserVisibility PhoneNumbers { get; set; }
        public UserVisibility BirthDate { get; set; }
        public UserVisibility MaritalStatus { get; set; }
        public UserVisibility PoliticalViews { get; set; }
        public UserVisibility MemberOfParties { get; set; }
        public UserVisibility Nationality { get; set; }
        public UserVisibility Citizenship { get; set; }
        public UserVisibility MemberSince { get; set; }
        public UserVisibility Summary { get; set; }
        public UserVisibility Specialties { get; set; }
        public UserVisibility Photo { get; set; }
        public UserVisibility Activity { get; set; }
        public UserVisibility Reputation { get; set; }
        public UserVisibility Contact { get; set; }

        public UserDetailsVisibility()
        {
            UserName = UserVisibility.Hidden;
            Email = UserVisibility.Connected;
            PhoneNumbers = UserVisibility.Connected;
            Contact = UserVisibility.Registered;
        }
    }
}
