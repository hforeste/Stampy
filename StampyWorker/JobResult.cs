using StampyCommon.Models;
using System.Collections.Generic;

namespace StampyWorker
{
    public class JobResult
    {
        public Status JobStatus { get; set; }
        public string Message { get; set; }

        public Dictionary<string, object> ResultDetails { get; set; }

        public JobResult()
        {
            ResultDetails = new Dictionary<string, object>();
            Message = "";
        }
    }
}
