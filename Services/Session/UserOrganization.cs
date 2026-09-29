using Framework.Enums;

namespace Services.Session
{
    public class UserOrganization
    {
        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }
        public bool IsMember { get; set; }
        public UserRoles Permission { get; set; }
        public bool IsPrivate { get; set; }
    }
}
