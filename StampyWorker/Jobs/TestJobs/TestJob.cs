using StampyCommon;
using StampyCommon.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyWorker.Jobs
{
    internal class TestJob : IJob
    {
        private ICloudStampyLogger _logger;
        private CloudStampyParameters _args;

        public TestJob(ICloudStampyLogger logger, CloudStampyParameters args)
        {
            _logger = logger;
            _args = args;
        }

        public async Task<JobResult> Execute()
        {
            JobResult jobResult;

            if (!Directory.Exists(_args.BuildPath))
            {
                _logger.WriteError(_args, $"Failed to access fileshare: {_args.BuildPath}");
                jobResult = new JobResult
                {
                    JobStatus = Status.Failed,
                    Message = $"Failed to access fileshare: {_args.BuildPath}"
                };

                return jobResult;
            }

            if (!TryCreateTestConfig($@"\\antaresdeployment\PublicLockbox\{_args.CloudName}"))
            {
                jobResult = new JobResult();
                jobResult.JobStatus = Status.Failed;
                jobResult.Message = $"Failed to create TestCommon config file";
                return jobResult;
            }

            if (_args.TestCategories == null || _args.TestCategories.Count == 0)
            {
                jobResult = new JobResult();
                jobResult.JobStatus = Status.Failed;
                jobResult.Message = $"Test names were not specified";
                return jobResult;
            }

            var testClient = TestClientFactory.GetTestClient(_logger, _args);
            var jobResults = new List<JobResult>();
            
            foreach (var categoryGroup in _args.TestCategories)
            {
                var concurrentTasks = new List<Task<JobResult>>();

                foreach (var category in categoryGroup)
                {
                    concurrentTasks.Add(testClient.ExecuteTestAsync(category));
                }

                await Task.WhenAll(concurrentTasks);

                foreach (var finishedTask in concurrentTasks)
                {
                    jobResults.Add(await finishedTask);
                }
            }

            var aggregatedJobResult = new JobResult
            {
                JobStatus = jobResults.All(j => j.JobStatus == Status.Passed) ? Status.Passed : Status.Failed,
                Message = string.Join(";", jobResults.Select(j  => j.Message))
            };

            return aggregatedJobResult;
        }

        private bool TryCreateTestConfig(string fileShareLocation)
        {
            try
            {
                if (!Directory.Exists(fileShareLocation))
                {
                    throw new ArgumentException($"{fileShareLocation} does not exist");
                }
                var cloudTestCommonConfiguration = Path.Combine(fileShareLocation, "TestCommon.dll.config");
                if (!File.Exists(cloudTestCommonConfiguration))
                {
                    File.Copy(@"\\AntaresDeployment\PublicLockBox\TestCommon\TestCommon.dll.config", cloudTestCommonConfiguration);
                }
                else
                {
                    _logger.WriteInfo(_args, $"TestCommon configuration file already exist in {fileShareLocation}");
                }

                return true;
            }catch(Exception ex)
            {
                _logger.WriteError(_args, $"Failed to create TestCommon config file at {fileShareLocation}", ex);
            }

            return false;
        }
    }
}
