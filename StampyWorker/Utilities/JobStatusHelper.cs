using StampyCommon.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyWorker.Utilities
{
    internal class JobStatusHelper
    {
        public async static Task<JobResult> StartPeriodicStatusUpdates(IJob parent, IJob childJob, Task<JobResult> childTask)
        {
            return await StartPeriodicStatusUpdates(parent, new IJob[] { childJob }, new Task<JobResult>[] { childTask });
        }

        public async static Task<JobResult> StartPeriodicStatusUpdates(IJob parent, IEnumerable<IJob> childJobs, IEnumerable<Task<JobResult>> childTasks)
        {
            JobResult jResult = new JobResult();
            var compositeChildTask = Task.WhenAll(childTasks);

            while (true)
            {
                var periodicTimerTask = Task.Run(async () => await Task.Delay(TimeSpan.FromMinutes(1)));

                var tasks = new List<Task>{ periodicTimerTask, compositeChildTask };

                var finishedTask = await Task.WhenAny(tasks);

                parent.JobStatus = jResult.JobStatus = DetermineOverallJobStatus(childJobs);
                parent.ReportUri = string.Join(",", childJobs.Select(j => j.ReportUri));

                if (finishedTask.Id == compositeChildTask.Id)
                {
                    break;
                }
            }

            var childTaskResults = await compositeChildTask;
            jResult.Message = string.Join(",", childTaskResults.Select(r => r.Message));

            foreach (var item in childTaskResults)
            {
                foreach (var pair in item.ResultDetails)
                {
                    jResult.ResultDetails.Add(pair.Key, pair.Value);
                }
            }

            return jResult;
        }

        public async static Task<JobResult> StartPeriodicStatusUpdates(IJob parent, IJob childJob, Func<JobResult> childFunc)
        {
            var childTask = Task.Run(childFunc);
            return await StartPeriodicStatusUpdates(parent, childJob, childTask);
        }

        private static Status DetermineOverallJobStatus(IEnumerable<IJob> childJobs)
        {
            if (childJobs.GroupBy((j) => j.JobStatus).Count() == 1)
            {
                return childJobs.First().JobStatus;
            }
            else if (childJobs.Any(j => j.JobStatus == Status.Failed))
            {
                return Status.Failed;
            }
            else if (childJobs.Any(j => j.JobStatus == Status.Cancelled))
            {
                return Status.Cancelled;
            }
            else
            {
                return Status.InProgress;
            }
        }
    }
}
