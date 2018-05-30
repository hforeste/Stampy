using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using StampyCommon;
using StampyCommon.Loggers;
using StampyCommon.Models;

namespace StampyWorker
{
    public static class JobRunner
    {
        [FunctionName("JobRunner")]
        [return: Queue("stampy-jobs-finished")]
        public static async Task<StampyResult> Run([QueueTrigger("stampy-jobs", Connection = "StampyStorageConnectionString")]CloudStampyParameters myQueueItem)
        {
            StampyResult result = null;
            var eventsLogger = new StampyWorkerEventsKustoLogger(new KustoLoggingConfiguration());
            var resultsLogger = new StampyResultsLogger(new KustoLoggingConfiguration());
            JobResult jobResult = null;
            Exception jobException = null;

            var sw = new Stopwatch();
            var job = JobFactory.GetJob(eventsLogger, myQueueItem);

            if (job != null)
            {
                try
                {
                    sw.Start();
                    jobResult = await job.Execute();
                    sw.Stop();
                }
                catch (Exception ex)
                {
                    jobException = ex;
                    jobResult = new JobResult { Status = StampyCommon.Models.JobResult.Failed };
                    eventsLogger.WriteError(myQueueItem, "Error while running job", ex);
                }
                finally
                {
                    result = new StampyResult
                    {
                        BuildPath = myQueueItem.BuildPath,
                        CloudName = myQueueItem.CloudName,
                        DeploymentTemplate = myQueueItem.DeploymentTemplate,
                        JobId = myQueueItem.JobId,
                        RequestId = myQueueItem.RequestId,
                        Result = jobResult.Status,
                        StatusMessage = jobResult.Message
                    };

                    resultsLogger.WriteResult(myQueueItem, jobResult.Status.ToString(), (int)sw.Elapsed.TotalMinutes, jobException);
                }
            }
            else
            {
                eventsLogger.WriteError(myQueueItem, "Cannot run this job");
            }

            return result;
        }
    }
}
