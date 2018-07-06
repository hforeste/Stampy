using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
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
        [return: Queue("test-jobs-staging")]
        public static async Task<CloudStampyParameters> DeployToCloudService([QueueTrigger("deployment-jobs", Connection = "StampyStorageConnectionString")]CloudStampyParameters myQueueItem)
        {
            var nextJob = myQueueItem.Copy();

            if ((myQueueItem.JobType & StampyJobType.Deploy) == StampyJobType.Deploy && (myQueueItem.FlowStatus == Status.InProgress || myQueueItem.FlowStatus == default(Status)))
            {

                var jobResult = await ExecuteJob(myQueueItem, StampyJobType.Deploy, TimeSpan.FromMinutes(240));
                nextJob.FlowStatus = jobResult.JobStatus == Status.Passed ? Status.InProgress : jobResult.JobStatus;
            }

            nextJob.JobId = Guid.NewGuid().ToString();
            return nextJob;
        }

        [FunctionName("ServiceCreator")]
        public static async Task CreateCloudService([QueueTrigger("service-creation-jobs", Connection = "StampyStorageConnectionString")]CloudStampyParameters request, [Queue("deployment-jobs")]ICollector<CloudStampyParameters> deploymentJobsQueue)
        {
            JobResult result;

            if ((request.JobType & StampyJobType.CreateService) == StampyJobType.CreateService && (request.FlowStatus == Status.InProgress || request.FlowStatus == default(Status)))
            {
                result = await ExecuteJob(request, StampyJobType.CreateService, TimeSpan.FromMinutes(10));
                if (result.JobStatus == Status.Passed)
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
                        TestCategories = request.TestCategories,
                        FlowStatus = result.JobStatus == Status.Passed ? Status.InProgress : result.JobStatus
                    };

                    var stampJob = new CloudStampyParameters
                    {
                        RequestId = request.RequestId,
                        JobId = Guid.NewGuid().ToString(),
                        BuildPath = request.BuildPath,
                        CloudName = $"{request.CloudName}",
                        DeploymentTemplate = "Antares_StompDeploy.xml",
                        JobType = request.JobType,
                        TestCategories = request.TestCategories,
                        FlowStatus = result.JobStatus == Status.Passed ? Status.InProgress : result.JobStatus
                    };

                    deploymentJobsQueue.Add(geomasterJob);
                    deploymentJobsQueue.Add(stampJob);
                }
            }
            else
            {
                var p = request.Copy();
                p.JobId = Guid.NewGuid().ToString();
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
            if (string.IsNullOrWhiteSpace(request.JobId))
            {
                request.JobId = Guid.NewGuid().ToString();
            }
            var serviceCreationJob = request.Copy();

            if ((request.JobType & StampyJobType.Build) == StampyJobType.Build)
            {
                var result = await ExecuteJob(request, StampyJobType.Build, TimeSpan.FromMinutes(120));

                if (result.ResultDetails.TryGetValue("Build Share", out object val))
                {
                    serviceCreationJob.BuildPath = (string)val;
                }
                else
                {
                    eventsLogger.WriteError("Build Share is empty");
                }

                serviceCreationJob.FlowStatus = result.JobStatus == Status.Passed ? Status.InProgress : result.JobStatus;
            }
            serviceCreationJob.JobId = Guid.NewGuid().ToString();
            return serviceCreationJob;
        }

        [FunctionName("TestRunner")]
        [return: Queue("stampy-jobs-finished")]
        public static async Task<CloudStampyParameters> TestRunner([QueueTrigger("test-jobs", Connection = "StampyStorageConnectionString")]CloudStampyParameters request)
        {
            var job = request.Copy();
            job.JobId = Guid.NewGuid().ToString();

            if ((request.JobType & StampyJobType.Test) == StampyJobType.Test && (request.FlowStatus == Status.InProgress || request.FlowStatus == default(Status)))
            {
                if (request.TestCategories.Any() && !string.IsNullOrWhiteSpace(request.BuildPath) && !string.IsNullOrWhiteSpace(request.CloudName))
                {
                    var result = await ExecuteJob(request, StampyJobType.Test, TimeSpan.FromMinutes(180));
                    job.FlowStatus = result.JobStatus == Status.Passed ? Status.InProgress : Status.Failed;
                    if ((job.JobType & StampyJobType.RemoveResources) != StampyJobType.RemoveResources)
                    {
                        job.JobType = job.JobType | StampyJobType.RemoveResources;
                    }
                    job.ExpiryDate = DateTime.UtcNow.AddHours(1).ToString();
                }
            }
            return job;
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

        [FunctionName("CompositeParameters")]
        public static async Task ComposeTestBuildParameters([TimerTrigger("0 */1 * * * *")]TimerInfo timer)
        {
            var storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("StampyStorageConnectionString"));
            var queueClient = storageAccount.CreateCloudQueueClient();
            var stagingQueue = queueClient.GetQueueReference("test-jobs-staging");
            var testJobsQueue = queueClient.GetQueueReference("test-jobs");

            var jobsPerRequest = new Dictionary<string, List<CloudQueueMessage>>();

            var queueMessages = await stagingQueue.GetMessagesAsync(32, TimeSpan.FromSeconds(10), null, null);

            foreach (var message in queueMessages)
            {
                var stampyJob = JsonConvert.DeserializeObject<CloudStampyParameters>(message.AsString);

                List<CloudQueueMessage> csp;

                if (!jobsPerRequest.TryGetValue(stampyJob.RequestId, out csp))
                {
                    csp = new List<CloudQueueMessage>
                        {
                            message
                        };

                    jobsPerRequest.Add(stampyJob.RequestId, csp);
                }
                else
                {
                    csp.Add(message);
                    jobsPerRequest[stampyJob.RequestId] = csp;
                }

                //if both geomaster and stamp deployment jobs are done then send a job to the TestBuild function
                if (csp.Count >= 2)
                {
                    var testJobParameters = stampyJob.Copy();
                    testJobParameters.JobId = Guid.NewGuid().ToString();
                    testJobParameters.FlowStatus = csp
                        .Select(deploymentJobQueueMessage => JsonConvert.DeserializeObject<CloudStampyParameters>(deploymentJobQueueMessage.AsString))
                        .All(deploymentJob => deploymentJob.FlowStatus == Status.InProgress) ? Status.InProgress : Status.Failed;

                    var queueMessageContext = new OperationContext();
                    var testJobMessage = new CloudQueueMessage(testJobParameters.ToJsonString());
                    await testJobsQueue.AddMessageAsync(testJobMessage, null, null, null, queueMessageContext);

                    if (queueMessageContext.LastResult.HttpStatusCode == (int)HttpStatusCode.Created)
                    {
                        var deleteContext = new OperationContext();
                        var queueMessageDeletionTasks = new List<Task>();
                        csp.ForEach(deploymentJobQueueMessage => queueMessageDeletionTasks.Add(stagingQueue.DeleteMessageAsync(deploymentJobQueueMessage, null, deleteContext)));

                        await Task.WhenAll(queueMessageDeletionTasks);

                        foreach (var item in deleteContext.RequestResults)
                        {
                            if (item.HttpStatusCode != (int)HttpStatusCode.NoContent)
                            {
                                eventsLogger.WriteError($"Failed to delete deployment-job from {stagingQueue.Name}. Storage Status Code: {item.HttpStatusCode}-{item.HttpStatusMessage}");
                            }
                        }
                    }
                    else
                    {
                        eventsLogger.WriteError($"Failed to add job to {testJobsQueue.Name}. Storage Status Code: {queueMessageContext.LastResult.HttpStatusCode}-{queueMessageContext.LastResult.HttpStatusMessage}", queueMessageContext.LastResult.Exception);
                    }
                }
            }
        }

        private static async Task<JobResult> ExecuteJob(CloudStampyParameters queueItem, StampyJobType requestedJobType, TimeSpan timeout)
        {
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
                    var jobResultTask = job.Execute();
                    var timeoutTask = Task.Run(async () => await Task.Delay(timeout));

                    Task finishedTask;

                    while (true)
                    {
                        var timerTask = Task.Run(async () => await Task.Delay(TimeSpan.FromMinutes(1)));
                        finishedTask = await Task.WhenAny(new Task[] { jobResultTask,  timerTask, timeoutTask });

                        //log the progress of the job
                        resultsLogger.WriteJobProgress(queueItem.RequestId, queueItem.JobId, requestedJobType, job.JobStatus.ToString(), job.ReportUri);

                        if (finishedTask.Id == jobResultTask.Id)
                        {
                            //job is done
                            jobResult = await jobResultTask;
                            break;
                        }
                        else if (finishedTask.Id == timeoutTask.Id)
                        {
                            jobResult = new JobResult { JobStatus = Status.Cancelled };
                            eventsLogger.WriteError(queueItem, $"Cancel job. {job.GetType().ToString()} took longer than timeout of {timeout.TotalMinutes.ToString()}mins");
                            var isCancelled = await job.Cancel();

                            if (!isCancelled)
                            {
                                eventsLogger.WriteError(queueItem, "Error while cancelling job");
                            }
                            else
                            {
                                eventsLogger.WriteInfo(queueItem, "Cancelled job successfully");
                            }

                            break;
                        }
                    }
                    sw.Stop();
                }
                catch (Exception ex)
                {
                    jobException = ex;
                    jobResult = new JobResult { JobStatus = Status.Failed };
                    eventsLogger.WriteError(queueItem, "Error while running job", ex);
                    throw;
                }
                finally
                {
                    resultsLogger.WriteResult(queueItem, jobResult.JobStatus.ToString(), (int)sw.Elapsed.TotalMinutes, jobException);
                }
            }
            else
            {
                eventsLogger.WriteError(queueItem, "Cannot run this job");
            }

            return jobResult;
        }
    }
}
