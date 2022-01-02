using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestSubjst.LogicalExpressionsTestbed
{
    class Class2
    {
        public void Method1(UserDto user /*more args*/)
        {
            if (user.IsSuspended || user.ActiveRole != Roles.Admin || user.CredentialsStartDate > DateTime.Now || DateTime.Now > user.CredentialsEndDate)
            {
                return;
            }

            // following code is filler, it is of no interest to us and wil not be featured in similarity analysis result

            if (DateTime.Today.DayOfWeek == DayOfWeek.Sunday
                && false == user.Roles.Contains(Roles.Auditor))
            {
                return;
            }

        }
    }
}
