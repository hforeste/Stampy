using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyWorker.Utilities
{
    interface IBuildClient
    {
        Task<JobResult> ExecuteBuild(string dpkPath = null);
    }
}
