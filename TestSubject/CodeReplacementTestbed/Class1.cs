namespace TestSubject.CodeReplacementTestbed
{
    class Class1
    {
        private readonly ReportSchedulingSystem reportSchedulingSystem; // will normaly be injected
        public void TaxesReport() 
        {
            string reportName = "T205: My tax forms";
            int userIdInAccountingSystem = 5;

            var scheduleId = reportSchedulingSystem.ScheduleReport(reportName, false, userIdInAccountingSystem, null, null, null);
        }

        public void OrderedReportsHistoryReport()
        {
            string reportName = "S3: Users ordered reports history";

            var scheduleId = reportSchedulingSystem.ScheduleReport(reportName, null, null, null, null, null);
        }
    }
}
