using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestSubject.CodeReplacementTestbed
{
    class Class3
    {
        private readonly ReportSchedulingSystem reportSchedulingSystem; // will normaly be injected
        private readonly dynamic config;
        public void AnnualSalesReport()
        {
            string usesPositionInSales = "Sales Analyst";
            string reportName = "SA7: Annual sales report";
            int salesId = 8;
            int suplyId = 8;

            var scheduleId = reportSchedulingSystem.ScheduleReport(reportName,
                GetPriorityFlagFromConfigBasedOnUsersPositionInSales(usesPositionInSales), null, null, salesId, suplyId);
        }

        private bool GetPriorityFlagFromConfigBasedOnUsersPositionInSales(string usePositionInSales)
        {
            return config.userCreation.immediatelyActivateUserInPositions.Contains(usePositionInSales);
        }
    }
}
