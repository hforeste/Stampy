using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyWorker.Jobs
{
    interface ITestClient
    {
        Task<JobResult> ExecuteTestsAsync(string[] tests);
    }
}
