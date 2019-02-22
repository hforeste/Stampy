﻿using System;
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
| distinct TimeStamp, RequestId, User, BuildPath , DpkPath, Client , JobTypes, DeploymentTemplatePerCloudService , TestCategories
| order by TimeStamp desc
";
        public static string GetJobDetails(string requestId)
        {
            return $@"StampyResults
| where RequestId == ""{requestId}""
| extend EndTime = iif(isnotnull(JobDurationMinutes), TimeStamp, datetime(null))
| summarize arg_max(TimeStamp, Status), StartTime = min(TimeStamp), EndTime = max(EndTime), ReportUri = any(ReportUri), JobDurationInMinutes = any(JobDurationMinutes), ExceptionType = any(ExceptionType), ExceptionDetails = any(ExceptionDetails) by JobId, JobType
| order by TimeStamp asc
| project-away TimeStamp";
        }

    }
}
