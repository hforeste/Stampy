using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
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
            eventsLogger = new StampyWorkerEventsKustoLogger(new LoggingConfiguration());
            resultsLogger = new StampyResultsLogger(new LoggingConfiguration());
        }

        [FunctionName("DeploymentCreator")]
        [return: Queue("test-jobs")]
        public static async Task<CloudStampyParameters> DeployToCloudService([QueueTrigger("deployment-jobs", Connection = "StampyStorageConnectionString")]CloudStampyParameters myQueueItem)
        {
            if ((myQueueItem.JobType & StampyJobType.Deploy) == StampyJobType.Deploy)
            {
                var stampyResult = await ExecuteJob(myQueueItem, StampyJobType.Deploy);
                if (stampyResult.Result != StampyCommon.Models.JobResult.Passed)
                {
                    throw new Exception($"Failed to deploy to {myQueueItem.CloudName}");
                }
            }

            var nextJob = myQueueItem.Copy();
            nextJob.JobId = Guid.NewGuid().ToString();
            return nextJob;
        }

        [FunctionName("ServiceCreator")]
        public static async Task CreateCloudService([QueueTrigger("service-creation-jobs", Connection = "StampyStorageConnectionString")]CloudStampyParameters request, [Queue("deployment-jobs")]ICollector<CloudStampyParameters> deploymentJobsQueue)
        {
            StampyCommon.Models.JobResult result;

            if ((request.JobType & StampyJobType.CreateService) == StampyJobType.CreateService)
            {
                result = (await ExecuteJob(request, StampyJobType.CreateService)).Result;
                if (result == StampyCommon.Models.JobResult.Passed)
                {
                    //TODO check the deployment template set in the parameters. Use that to determine what kind of service to create

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
            }
            else
            {
                var p = new CloudStampyParameters
                {
                    RequestId = request.RequestId,
                    JobId = Guid.NewGuid().ToString(),
                    BuildPath = request.BuildPath,
                    CloudName = request.CloudName,
                    JobType = request.JobType,
                    TestCategories = request.TestCategories,
                    DeploymentTemplate = request.DeploymentTemplate,
                    DpkPath = request.DpkPath
                };

                deploymentJobsQueue.Add(p);
            }
        }

        /// <summary>
        /// A proxy function to the OnPrem Build Agents
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [FunctionName("BuildChanges")]
        [return: Queue("service-creation-jobs")]
        public static async Task<CloudStampyParameters> BuildChanges([QueueTrigger("build-jobs", Connection = "StampyStorageConnectionString")]CloudStampyParameters request)
        {
            var serviceCreationJob = request.Copy();

            if ((request.JobType & StampyJobType.Build) == StampyJobType.Build)
            {
                var result = await ExecuteJob(request, StampyJobType.Build);

                if (result.JobResultDetails.TryGetValue("Build Share", out object val))
                {
                    serviceCreationJob.BuildPath = (string)val;
                }
                else
                {
                    eventsLogger.WriteError("Build Share is empty");
                }
            }

            serviceCreationJob.JobId = Guid.NewGuid().ToString();
            return serviceCreationJob;
        }

        [FunctionName("TestBuild")]
        [return: Queue("stampy-jobs-finished")]
        public static async Task<CloudStampyParameters> TestBuild([QueueTrigger("test-jobs", Connection = "StampyStorageConnectionString")]CloudStampyParameters request)
        {
            var finishedJob = request.Copy();
            finishedJob.JobId = Guid.NewGuid().ToString();
            if ((finishedJob.JobType & StampyJobType.RemoveResources) == StampyJobType.RemoveResources)
            {
                finishedJob.JobType = finishedJob.JobType | StampyJobType.RemoveResources;
            }
            finishedJob.ExpiryDate = DateTime.UtcNow.AddHours(1).ToString();
            return finishedJob;
        }

        [FunctionName("ResourceCleaner")]
        public static async Task RemoveResources([TimerTrigger("0 */10 * * * *")]TimerInfo timer)
        {
            var storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("StampyStorageConnectionString"));
            var queueClient = storageAccount.CreateCloudQueueClient();
            var queue = queueClient.GetQueueReference("stampy-jobs-finished");
            List<CloudQueueMessage> messages = new List<CloudQueueMessage>();

            int? messageCount;
            int tries = 0;
            do
            {
                await queue.FetchAttributesAsync();
                messageCount = queue.ApproximateMessageCount;
                var queueMessages = await queue.GetMessagesAsync(32);
                foreach (var message in queueMessages)
                {
                    if (!messages.Any(m => m.Id == message.Id))
                    {
                        messages.Add(message);
                    }
                }
                
                tries++;
            } while (messageCount.HasValue && messages.Count < messageCount.GetValueOrDefault() && tries < 3);

            foreach (var message in messages)
            {
                var stampyJob = JsonConvert.DeserializeObject<CloudStampyParameters>(message.AsString);
                if (DateTime.Parse(stampyJob.ExpiryDate) >= DateTime.UtcNow)
                {
                    //await queue.DeleteMessageAsync(message);
                    //TODO delete resources
                }
            }
        }

        private static async Task<StampyResult> ExecuteJob(CloudStampyParameters queueItem, StampyJobType requestedJobType)
        {
            StampyResult result = null;
            JobResult jobResult = null;
            Exception jobException = null;

            var sw = new Stopwatch();
            var job = JobFactory.GetJob(eventsLogger, queueItem, requestedJobType);

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
                        StatusMessage = jobResult.Message,
                        JobResultDetails = jobResult.ResultDetails
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
