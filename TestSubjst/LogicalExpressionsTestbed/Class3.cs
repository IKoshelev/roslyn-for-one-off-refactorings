using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestSubjst.LogicalExpressionsTestbed
{
    class Class3
    {
        public void Method1(UserDto u /*more args*/)
        {
            if (u.IsSuspended
                || u.ActiveRole == null
                || !(u.CredentialsStartDate < DateTime.Now && DateTime.Now < u.CredentialsEndDate)
                || u.ActiveRole != Roles.Admin)
            {
                return;
            }

            // following code is filler, it is of no interest to us and wil not be featured in similarity analysis result

            if (u.Roles.Any() 
                || u.PhysicalAddress == "" || u.EmailAddress == "" 
                || u.HasPendingPermissions)
            {
                return;
            }

        }
    }
}
