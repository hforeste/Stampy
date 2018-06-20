using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
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
        [return: Queue("stampy-jobs-finished")]
        public static async Task<StampyResult> DeployToCloudService([QueueTrigger("deployment-jobs", Connection = "StampyStorageConnectionString")]CloudStampyParameters myQueueItem)
        {
            if ((myQueueItem.JobType & StampyJobType.Deploy) == StampyJobType.Deploy)
            {
                return await ExecuteJob(myQueueItem, StampyJobType.Deploy);
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
        public static async Task TestBuild([QueueTrigger("test-jobs", Connection = "StampyStorageConnectionString")]CloudStampyParameters request)
        {
            throw new NotImplementedException();
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
