using System.Collections.Generic;

namespace StampyWorker
{
    internal class JobResult
    {
        public StampyCommon.Models.JobResult Status { get; set; }
        public string Message { get; set; }

        public Dictionary<string, object> ResultDetails { get; set; }

        public JobResult()
        {
            Message = "";
        }
    }
}
