﻿using StampyCommon;
using StampyCommon.Models;
using StampyWorker.Jobs.BuildJobs;
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
        IBuildClient _buildClient;

        public BuildJob(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs)
        {
            _logger = logger;
            _args = cloudStampyArgs;

            if (string.IsNullOrWhiteSpace(_args.DpkPath))
            {
                _buildClient = new LabMachineBuildClient(_logger, _args);
            }
            else
            {
                _buildClient = new OneBranchBuildClient(_logger, _args);
            }
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
            var jResult = await JobStatusHelper.StartPeriodicStatusUpdates(this, (IJob)_buildClient, _buildClient.ExecuteBuild());
            object buildPath = null;
            if (jResult.ResultDetails.TryGetValue("Build Share", out buildPath))
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
