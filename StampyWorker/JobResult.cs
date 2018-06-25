using StampyCommon.Models;
using System.Collections.Generic;

namespace StampyWorker
{
    internal class JobResult
    {
        public Status JobStatus { get; set; }
        public string Message { get; set; }

        public Dictionary<string, object> ResultDetails { get; set; }

        public JobResult()
        {
            Message = "";
        }
    }
}
