using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyCommon.Models
{
    public class StampyResult
    {
        public string RequestId { get; set; }
        public string JobId { get; set; }
        public string BuildPath { get; set; }
        public string DeploymentTemplate { get; set; }
        public string CloudName { get; set; }
        public JobResult Result { get; set; }
        public string StatusMessage { get; set; }
        public Dictionary<string, object> JobResultDetails { get; set; }
    }

    public enum JobResult
    {
        Cancelled, Failed, Passed, InProgress, None
    }
}
