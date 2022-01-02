using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestSubjst
{
    public static class Roles
    {
        public const string Admin = "Admin";
        public const string Auditor = "Auditor";
    }

    public class UserDto
    {
        public string[] Roles { get; set; }
        public string ActiveRole { get; set; }
        public DateTime CredentialsStartDate { get; set; }
        public DateTime CredentialsEndDate { get; set; }
        public bool IsSuspended { get; set; }
        public string PhysicalAddress { get; set; }
        public string EmailAddress { get; set; }
        public bool HasPendingPermissions { get; set; }
    }
}
