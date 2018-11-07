using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using StampyCommon;
using StampyCommon.Loggers;
using StampyCommon.Models;
using StampyWorker.Utilities;

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
        [return: Queue("finished-deployment-jobs")]
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
                if (string.IsNullOrWhiteSpace(request.CloudName))
                {
                    const string alphanumericalchars = "abcdefghijklmnopqrstuvwxyz1234567890";
                    var rng = new RNGCryptoServiceProvider();
                    byte[] xx = new byte[16];
                    rng.GetBytes(xx);
                    char[] y = new char[8];
                    for (int i = 0; i < y.Length; i++)
                    {
                        y[i] = alphanumericalchars[(xx[i] % alphanumericalchars.Length)];
                    }
                    request.CloudName = $"stampy-{new string(y)}";
                }

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
            var nextJob = request.Copy();
            nextJob.JobId = Guid.NewGuid().ToString();

            if ((request.JobType & StampyJobType.Test) == StampyJobType.Test && (request.FlowStatus == Status.InProgress || request.FlowStatus == default(Status)))
            {
                if (request.TestCategories.Any() && !string.IsNullOrWhiteSpace(request.BuildPath) && !string.IsNullOrWhiteSpace(request.CloudName))
                {
                    var resultTasks = new List<Task<JobResult>>();
                    var results = new List<JobResult>();

                    foreach (var testCategorySet in request.TestCategories)
                    {
                        foreach (var testCategory in testCategorySet)
                        {
                            var testJob = request.Copy();
                            testJob.JobId = Guid.NewGuid().ToString();
                            testJob.TestCategories = new List<List<string>> { new List<string> { testCategory } };

                            //test should timeout after 180 minutes. 
                            //TODO double check if function runtime has default timeout
                            resultTasks.Add(ExecuteJob(testJob, StampyJobType.Test, TimeSpan.FromHours(5)));
                        }

                        var testCategoryResults = await Task.WhenAll(resultTasks);
                        resultTasks.Clear();
                        results.AddRange(testCategoryResults);
                    }  

                    nextJob.FlowStatus = JobStatusHelper.DetermineOverallJobStatus(results);

                    if ((nextJob.JobType & StampyJobType.RemoveResources) != StampyJobType.RemoveResources)
                    {
                        nextJob.JobType = nextJob.JobType | StampyJobType.RemoveResources;
                    }
                    nextJob.ExpiryDate = DateTime.UtcNow.AddHours(1).ToString();
                }
            }

            return nextJob;
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
            var finishedDeploymentJobs = queueClient.GetQueueReference("finished-deployment-jobs");
            var testJobsQueue = queueClient.GetQueueReference("test-jobs");
            var generalOperationContext = new OperationContext();
            var jobsPerRequest = new Dictionary<string, List<CloudQueueMessage>>();

            var queueMessages = await finishedDeploymentJobs.GetMessagesAsync(32, TimeSpan.FromSeconds(10), null, null);

            foreach (var message in queueMessages)
            {
                var stampyJob = JsonConvert.DeserializeObject<CloudStampyParameters>(message.AsString);

                if ((stampyJob.JobType & StampyJobType.Deploy) != StampyJobType.Deploy)
                {
                    //note: don't use the same CloudQueueMessage object dequeued from one queue and enqueued in another and try to delete. As this will change the original message id and pop receipt
                    //and cause a 404
                    await testJobsQueue.AddMessageAsync(new CloudQueueMessage(stampyJob.ToJsonString()), null, null, null, generalOperationContext);
                    if (generalOperationContext.LastResult.HttpStatusCode == (int)HttpStatusCode.Created)
                    {
                        await finishedDeploymentJobs.DeleteMessageAsync(message);
                    }
                    continue;
                }

                List<CloudQueueMessage> parameters;

                if (!jobsPerRequest.TryGetValue(stampyJob.RequestId, out parameters))
                {
                    parameters = new List<CloudQueueMessage>
                        {
                            message
                        };

                    jobsPerRequest.Add(stampyJob.RequestId, parameters);
                }
                else
                {
                    parameters.Add(message);
                    jobsPerRequest[stampyJob.RequestId] = parameters;
                }

                //if both geomaster and stamp deployment jobs are done then send a job to the TestBuild function
                if (parameters.Count == 2)
                {
                    var testJobParameters = stampyJob.Copy();
                    testJobParameters.JobId = Guid.NewGuid().ToString();
                    testJobParameters.FlowStatus = parameters
                        .Select(deploymentJobQueueMessage => JsonConvert.DeserializeObject<CloudStampyParameters>(deploymentJobQueueMessage.AsString))
                        .All(deploymentJob => deploymentJob.FlowStatus == Status.InProgress) ? Status.InProgress : Status.Failed;
                    var testJobMessage = new CloudQueueMessage(testJobParameters.ToJsonString());
                    await testJobsQueue.AddMessageAsync(testJobMessage, null, null, null, generalOperationContext);

                    if (generalOperationContext.LastResult.HttpStatusCode == (int)HttpStatusCode.Created)
                    {
                        var deleteContext = new OperationContext();
                        var queueMessageDeletionTasks = new List<Task>();
                        parameters.ForEach(deploymentJobQueueMessage => queueMessageDeletionTasks.Add(finishedDeploymentJobs.DeleteMessageAsync(deploymentJobQueueMessage, null, deleteContext)));

                        await Task.WhenAll(queueMessageDeletionTasks);

                        foreach (var item in deleteContext.RequestResults)
                        {
                            if (item.HttpStatusCode != (int)HttpStatusCode.NoContent)
                            {
                                eventsLogger.WriteError($"Failed to delete deployment-job from {finishedDeploymentJobs.Name}. Storage Status Code: {item.HttpStatusCode}-{item.HttpStatusMessage}");
                            }
                        }
                    }
                    else
                    {
                        eventsLogger.WriteError($"Failed to add job to {testJobsQueue.Name}. Storage Status Code: {generalOperationContext.LastResult.HttpStatusCode}-{generalOperationContext.LastResult.HttpStatusMessage}", generalOperationContext.LastResult.Exception);
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
                    var jobResultTask = Task.Run(() => job.Execute());
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
                    resultsLogger.WriteResult(queueItem.RequestId, queueItem.JobId, requestedJobType, jobResult.JobStatus.ToString(), (int)sw.Elapsed.TotalMinutes, jobException);
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
