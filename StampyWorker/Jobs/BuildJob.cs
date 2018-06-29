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

        public Status Status { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string ReportUri { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public Task<bool> Cancel()
        {
            throw new NotImplementedException();
        }

        public async Task<JobResult> Execute()
        {
            var buildClient = BuildClientFactory.GetBuildClient(_logger, _args);
            var jResult = await buildClient.ExecuteBuild();
            return jResult;
        }
    }
}
