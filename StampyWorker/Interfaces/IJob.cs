using StampyCommon;
using StampyCommon.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyWorker
{
    interface IJob
    {
        Status Status { get; set; }
        string ReportUri { get; set; }
        Task<JobResult> Execute();
        Task<bool> Cancel();
    }
}
