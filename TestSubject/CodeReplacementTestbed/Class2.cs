using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestSubject.CodeReplacementTestbed
{
    class Class2
    {
        private readonly ReportSchedulingSystem reportSchedulingSystem; // will normaly be injected
        public void PendingShippingOrdersRepport()
        {
            var scheduleId = reportSchedulingSystem.ScheduleReport("SH10: Pending shipping orders", null, userIdInSupplySystem: 8);
        }
    }
}
