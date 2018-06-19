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
        static StampyWorkerEventsKustoLogger eventsLogger;
        static StampyResultsLogger resultsLogger;
        static JobRunner()
        {
            eventsLogger = new StampyWorkerEventsKustoLogger(new KustoLoggingConfiguration());
            resultsLogger = new StampyResultsLogger(new KustoLoggingConfiguration());
        }

        [FunctionName("DeploymentCreator")]
        [return: Queue("stampy-jobs-finished")]
        public static async Task<StampyResult> DeployToCloudService([QueueTrigger("deployment-jobs", Connection = "StampyStorageConnectionString")]CloudStampyParameters myQueueItem)
        {
            if ((myQueueItem.JobType & StampyJobType.Deploy) == StampyJobType.Deploy)
            {
                return await ExecuteJob(myQueueItem);
            }

            var result = new StampyResult()
            {
                BuildPath = myQueueItem.BuildPath,
                CloudName = myQueueItem.CloudName,
                DeploymentTemplate = myQueueItem.DeploymentTemplate,
                JobId = myQueueItem.JobId,
                RequestId = myQueueItem.RequestId,
                Result = StampyCommon.Models.JobResult.Passed,
                StatusMessage = "Skipped deployment"
            };

            return result;
        }

        [FunctionName("ServiceCreator")]
        [return: Queue("deployment-jobs")]
        public static async Task CreateCloudService([QueueTrigger("service-creation-jobs", Connection = "StampyStorageConnectionString")]CloudStampyParameters request, [Queue("deployment-jobs")]ICollector<CloudStampyParameters> deploymentJobsQueue)
        {
            var result = (await ExecuteJob(request)).Result;

            if (result == StampyCommon.Models.JobResult.Passed)
            {
                if ((request.JobType & StampyJobType.Deploy) == StampyJobType.Deploy)
                {
                    //ServiceCreator creates the service using SetupPrivateStampWithGeo which essentially creates two cloud services. So enqueue a job to deploy to stamp and geomaster
                    var geomasterJob = new CloudStampyParameters
                    {
                        RequestId = request.RequestId,
                        JobId = Guid.NewGuid().ToString(),
                        BuildPath = request.BuildPath,
                        CloudName = $"{request.CloudName}geo",
                        DeploymentTemplate = "GeoMaster_StompDeploy.xml",
                        JobType = request.JobType,
                        TestCategories = request.TestCategories
                    };

                    var stampJob = new CloudStampyParameters
                    {
                        RequestId = request.RequestId,
                        JobId = Guid.NewGuid().ToString(),
                        BuildPath = request.BuildPath,
                        CloudName = $"{request.CloudName}",
                        DeploymentTemplate = "Antares_StompDeploy.xml",
                        JobType = request.JobType,
                        TestCategories = request.TestCategories
                    };

                    deploymentJobsQueue.Add(geomasterJob);
                    deploymentJobsQueue.Add(stampJob);
                }
                else
                {
                    //This is the case where we created a service but the end-user did not want to deploy to the service. We still need to create an empty job to continue the flow of functions.
                    var emptyJob = new CloudStampyParameters
                    {
                        RequestId = request.RequestId,
                        JobId = Guid.NewGuid().ToString(),
                        BuildPath = request.BuildPath,
                        CloudName = request.CloudName,
                        JobType = request.JobType,
                        TestCategories = request.TestCategories
                    };

                    deploymentJobsQueue.Add(emptyJob);
                }
            }
        }

        private static async Task<StampyResult> ExecuteJob(CloudStampyParameters queueItem)
        {
            StampyResult result = null;
            JobResult jobResult = null;
            Exception jobException = null;

            var sw = new Stopwatch();
            var job = JobFactory.GetJob(eventsLogger, queueItem);

            if (job != null)
            {
                try
                {
                    sw.Start();
                    eventsLogger.WriteInfo(queueItem, "start job");
                    jobResult = await job.Execute();
                    sw.Stop();
                }
                catch (Exception ex)
                {
                    jobException = ex;
                    jobResult = new JobResult { Status = StampyCommon.Models.JobResult.Failed };
                    eventsLogger.WriteError(queueItem, "Error while running job", ex);
                    throw;
                }
                finally
                {
                    result = new StampyResult
                    {
                        BuildPath = queueItem.BuildPath,
                        CloudName = queueItem.CloudName,
                        DeploymentTemplate = queueItem.DeploymentTemplate,
                        JobId = queueItem.JobId,
                        RequestId = queueItem.RequestId,
                        Result = jobResult.Status,
                        StatusMessage = jobResult.Message
                    };

                    resultsLogger.WriteResult(queueItem, jobResult.Status.ToString(), (int)sw.Elapsed.TotalMinutes, jobException);
                }
            }
            else
            {
                eventsLogger.WriteError(queueItem, "Cannot run this job");
            }

            return result;
        }
    }
}
