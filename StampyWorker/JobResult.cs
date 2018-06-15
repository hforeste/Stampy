namespace StampyWorker
{
    internal class JobResult
    {
        public StampyCommon.Models.JobResult Status { get; set; }
        public string Message { get; set; }

        public JobResult()
        {
            Message = "";
        }
    }
}
