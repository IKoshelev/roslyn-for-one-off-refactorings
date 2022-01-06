using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestSubject.CodeReplacementTestbed
{
    public class ReportSchedulingSystem
    {
        public static int NewIdCounter = 1;

        // The legacy method we've used before.
        // It is used across thousands of places in our code-base.
        // We would like to get rid of it in an automated manner
        [Obsolete]
        public int ScheduleReport(
            string reportName,
            bool? scheduleWithPriority,
            int? userIdInAccountingSystem = null,
            int? userIdInHrSystemSystem = null,
            int? userIdInSalesSystem = null,
            int? userIdInSuplySystem = null)
        {
            var newId = Interlocked.Increment(ref NewIdCounter);

            // actuall code

            return newId;
        }

        // The new method we would like to use now.
        public ScheduledReport ScheduleReport(
            string reportName,
            UserIdsAcrossSystems userIdsAcrossSystems = null,
            bool? scheduleWithPriority = null)
        {
            userIdsAcrossSystems = userIdsAcrossSystems ?? new UserIdsAcrossSystems();

            var newId = Interlocked.Increment(ref NewIdCounter);

            // actuall code

            return new ScheduledReport(
                newId,
                reportName,
                scheduleWithPriority ?? false,
                DateTime.Now);
        }
    }

    public record UserIdsAcrossSystems(
        int? userIdInAccountingSystem = null,
        int? userIdInHrSystemSystem = null,
        int? userIdInSalesSystem = null,
        int? userIdInSuplySystem = null);

    public record ScheduledReport(
        int id,
        string reportName,
        bool hasPriority,
        DateTime orderTimestamp);
}