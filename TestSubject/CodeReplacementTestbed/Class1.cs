namespace TestSubject.CodeReplacementTestbed
{
    class Class1
    {
        private readonly ReportSchedulingSystem reportSchedulingSystem; // will normaly be injected
        public void TaxesReport() 
        {
            string reportName = "T205: My tax forms";
            int id = 5;

            var scheduleId = reportSchedulingSystem.ScheduleReport(reportName, false, id, null, null, null);
        }

        public void OrderedReportsHistoryReport()
        {
            string reportName = "S3: Users ordered reports history";

            var scheduleId = reportSchedulingSystem.ScheduleReport(reportName: reportName, null, null, null, null, null);
        }
    }
}
