using StampyCommon;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyWorker.Jobs
{
    internal class ServiceCreationJob : IJob
    {
        ICloudStampyLogger _logger;
        CloudStampyParameters _parameters;
        private StringBuilder _statusMessageBuilder;
        private JobResult _result;

        public ServiceCreationJob(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs)
        {
            _logger = logger;
            _result = new JobResult();
            _result.Status = StampyCommon.Models.JobResult.None;
            _parameters = cloudStampyArgs;
            _statusMessageBuilder = new StringBuilder();
        }

        public Task<JobResult> Execute()
        {
            if (!File.Exists(AntaresDeploymentExecutablePath))
            {
                var ex = new FileNotFoundException("Cannot find file", Path.GetFileName(AntaresDeploymentExecutablePath));
                _logger.WriteError(_parameters, "Cannot find file", ex);
                throw ex;
            }

            var processStartInfo = new ProcessStartInfo();
            processStartInfo.FileName = AntaresDeploymentExecutablePath;
            processStartInfo.Arguments = $"SetupPrivateStampWithGeo {_parameters.CloudName} /SubscriptionId:b27cf603-5c35-4451-a33a-abba1a08c9c2 /VirtualDedicated:true /bvtCapableStamp:true /DefaultLocation:\"Central US\"";
            processStartInfo.UseShellExecute = false;
            processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processStartInfo.RedirectStandardError = true;
            processStartInfo.RedirectStandardOutput = true;

            _logger.WriteInfo(_parameters, $"Start {processStartInfo.FileName} {processStartInfo.Arguments}");

            using (var createProcess = Process.Start(processStartInfo))
            {
                createProcess.BeginErrorReadLine();
                createProcess.BeginOutputReadLine();
                createProcess.OutputDataReceived += new DataReceivedEventHandler(OutputReceived);
                createProcess.ErrorDataReceived += new DataReceivedEventHandler(ErrorReceived);
                createProcess.WaitForExit();
            }

            _result.Message = _statusMessageBuilder.ToString();
            _result.Status = StampyCommon.Models.JobResult.Passed;
            return Task.FromResult(_result);
        }

        private void OutputReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                if (e.Data.Contains("<ERROR>") || e.Data.Contains("<Exception>") || e.Data.Contains("Error:"))
                {
                    _statusMessageBuilder.AppendLine(e.Data);
                    _result.Status = StampyCommon.Models.JobResult.Failed;
                }
            }
        }

        private void ErrorReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _statusMessageBuilder.AppendLine(e.Data);
                _result.Status = StampyCommon.Models.JobResult.Failed;
            }
        }

        private string AntaresDeploymentExecutablePath
        {
            get
            {
                return Path.Combine(_parameters.BuildPath, @"hosting\Azure\RDTools\Tools\Antares\AntaresDeployment.exe");
            }
        }

    }
}
