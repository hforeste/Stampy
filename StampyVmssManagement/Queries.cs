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
| project TimeStamp, RequestId, User
| distinct TimeStamp, RequestId, User
";

    }
}
