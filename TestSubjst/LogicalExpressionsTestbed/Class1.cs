using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestSubjst.LogicalExpressionsTestbed
{
    class Class1
    {
        public void Method1(UserDto userDto /*more args*/)
        {
            if(userDto.IsSuspended 
                || userDto.ActiveRole == null
                || !(userDto.CredentialsStartDate < DateTime.Now && DateTime.Now < userDto.CredentialsEndDate)) 
            {
                return;
            }

            // following code is filler, it is of no interest to us and wil not be featured in similarity analysis result

            var obj = new
            {
                a = true,
                b = true,
                c = true
            };
            var d = true;
            var e = true;

            if (!(obj.a || obj.b || (obj.c & d)) && e)
            {
                return;
            }
        }
    }
}
