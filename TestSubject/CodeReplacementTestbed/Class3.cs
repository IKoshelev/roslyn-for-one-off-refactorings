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
            int userIdInSalesSystems= 8;

            var scheduleId = reportSchedulingSystem.ScheduleReport(reportName,
                GetPrioritFlagFromConfigBasedOnUsersPositionInSales(usesPositionInSales), null, null, userIdInSalesSystems, null);
        }

        private bool GetPrioritFlagFromConfigBasedOnUsersPositionInSales(string usePositionInSales)
        {
            return config.userCreation.immediatelyActivateUserInPositions.Contains(usePositionInSales);
        }
    }
}
