using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyVmssManagement
{
    internal class Queries
    {
        public static string ListJobs = @"
StampyClientRequests
| where TimeStamp >= ago(30d)
| distinct TimeStamp, RequestId, User, BuildPath , DpkPath, Client , JobTypes, CloudServiceNames, DeploymentTemplatePerCloudService , TestCategories
| order by TimeStamp desc
";
        public static string GetRequestStatus(string requestId)
        {
            return $@"StampyResults
| where RequestId == ""{requestId}""
| extend EndTime = iif(isnotnull(JobDurationMinutes), TimeStamp, datetime(null))
| summarize arg_max(TimeStamp, Status), StartTime = min(TimeStamp), EndTime = max(EndTime), ReportUri = any(ReportUri), JobDurationInMinutes = any(JobDurationMinutes), ExceptionType = any(ExceptionType), ExceptionDetails = any(ExceptionDetails) by JobId, JobType
| order by TimeStamp asc
| project-away TimeStamp";
        }

        public static string GetJobDetails(string requestId, string jobId)
        {
            return $@"StampyWorkerEvents 
| where TimeStamp >= ago(30d) and RequestId == ""{requestId}"" and JobId == ""{jobId}""
| order by TimeStamp asc";
        }

        public static string GetRequest(string requestId)
        {
            return $@"StampyClientRequests | where RequestId == ""{requestId}""";
        }

    }
}
