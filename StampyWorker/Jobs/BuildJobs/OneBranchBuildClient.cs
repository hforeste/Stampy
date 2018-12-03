using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StampyCommon;
using StampyCommon.Models;

namespace StampyWorker.Jobs.BuildJobs
{
    internal class OneBranchBuildClient : IBuildClient, IJob
    {
        private ICloudStampyLogger _logger;
        private CloudStampyParameters _args;

        public OneBranchBuildClient(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs)
        {
            _logger = logger;
            _args = cloudStampyArgs;
        }

        public Status JobStatus { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string ReportUri { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public Task<bool> Cancel()
        {
            throw new NotImplementedException();
        }

        public Task<JobResult> Execute()
        {
            throw new NotImplementedException();
        }

        public Task<JobResult> ExecuteBuild(CloudStampyParameters p)
        {
            throw new NotImplementedException();
        }

        public Task<JobResult> ExecuteBuild(string dpkPath = null)
        {
            throw new NotImplementedException();
        }
    }
}
