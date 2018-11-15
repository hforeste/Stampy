using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StampyCommon;
using StampyCommon.Models;

namespace StampyWorker.Jobs
{
    class ScavengerJob : IJob
    {
        private ICloudStampyLogger _logger;
        private JobResult _result;
        private CloudStampyParameters _parameters;
        private StringBuilder _statusMessageBuilder;

        public Status JobStatus { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string ReportUri { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public ScavengerJob(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs)
        {
            _logger = logger;
            _result = new JobResult();
            _parameters = cloudStampyArgs;
            _statusMessageBuilder = new StringBuilder();
        }

        public Task<bool> Cancel()
        {
            throw new NotImplementedException();
        }

        public Task<JobResult> Execute()
        {
            var definitionsPath = Path.Combine($@"\\AntaresDeployment\PublicLockBox\{_parameters.CloudName}\developer.definitions");
            if (File.Exists(definitionsPath))
            {
                string definitions = File.ReadAllText(definitionsPath);
                if (definitions.Contains(@"<redefine name=""VNET.Enabled"" value=""true"" />"))
                {

                }
            }
        }

        private Task GeoMasterDeletion()
        {
            if (true)
            {

            }
        }

        private Task StampDeletion()
        {

        }

        private void OutputReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                if (JobStatus == default(Status))
                {
                    JobStatus = Status.InProgress;
                }

                _logger.WriteInfo(_parameters, e.Data);
                if (e.Data.Contains("<ERROR>") || e.Data.Contains("<Exception>") || e.Data.Contains("Error:"))
                {
                    _statusMessageBuilder.AppendLine(e.Data);
                    _result.JobStatus = JobStatus = Status.Failed;
                }
            }
        }

        private void ErrorReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _statusMessageBuilder.AppendLine(e.Data);
                _result.JobStatus = JobStatus = Status.Failed;
            }
        }
    }
}
