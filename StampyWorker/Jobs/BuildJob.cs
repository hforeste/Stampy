using StampyCommon;
using StampyCommon.Models;
using StampyWorker.Utilities;
using System;
using System.Threading.Tasks;

namespace StampyWorker.Jobs
{
    /// <summary>
    /// BuildJob
    /// </summary>
    internal class BuildJob : IJob
    {
        ICloudStampyLogger _logger;
        CloudStampyParameters _args;

        public BuildJob(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs)
        {
            _logger = logger;
            _args = cloudStampyArgs;
        }

        public Status JobStatus { get; set; }
        public string ReportUri { get; set; }

        public Task<bool> Cancel()
        {
            //no op
            return Task.FromResult(true);
        }

        public async Task<JobResult> Execute()
        {
            var buildClient = BuildClientFactory.GetBuildClient(_logger, _args);
            var jResult = await JobStatusHelper.StartPeriodicStatusUpdates(this, (IJob)buildClient, buildClient.ExecuteBuild());
            object buildPath = null;
            if (jResult.ResultDetails.TryGetValue("Build Path", out buildPath))
            {
                _logger.WriteInfo(_args, $"Finished executing build at {(string)buildPath}");
            }
            else
            {
                _logger.WriteError(_args, "Failed to get build path");
            }
            return jResult;
        }
    }
}
