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
    internal class DeploymentJob : IJob
    {
        ICloudStampyLogger _logger;
        CloudStampyParameters _parameters;
        private List<string> _availableDeploymentTemplates;
        private StringBuilder _statusMessageBuilder;
        private JobResult _result;

        public DeploymentJob(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs)
        {
            _logger = logger;
            _result = new JobResult();
            _parameters = cloudStampyArgs;
            _statusMessageBuilder = new StringBuilder();
        }

        public Task<JobResult> Execute()
        {

            if (!AvailableDeploymentTemplates.Contains(_parameters.DeploymentTemplate))
            {
                _logger.WriteInfo(_parameters, $"Deployment template `{_parameters.DeploymentTemplate}` does not exist");
            }

            if (!File.Exists(DeployConsolePath))
            {
                _logger.WriteInfo(_parameters, $"Cannot find {DeployConsolePath}");
            }

            _logger.WriteInfo(_parameters, "Starting deployment...");

            var processStartInfo = new ProcessStartInfo();
            processStartInfo.FileName = DeployConsolePath;
            processStartInfo.Arguments = $"/LockBox={_parameters.CloudName} /Template={_parameters.DeploymentTemplate} /BuildPath={_parameters.BuildPath + @"\Hosting"} /TempDir={_deploymentArtificatsDirectory} /AutoRetry=true /LogFile={_azureLogFilePath}";
            processStartInfo.UseShellExecute = false;
            processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processStartInfo.RedirectStandardError = true;
            processStartInfo.RedirectStandardOutput = true;

            _logger.WriteInfo(_parameters, $"Start {processStartInfo.FileName} {processStartInfo.Arguments}");

            using (var deployProcess = Process.Start(processStartInfo))
            {
                deployProcess.BeginErrorReadLine();
                deployProcess.BeginOutputReadLine();
                deployProcess.OutputDataReceived += new DataReceivedEventHandler(OutputReceived);
                deployProcess.ErrorDataReceived += new DataReceivedEventHandler(ErrorReceived);
                deployProcess.WaitForExit();
            }

            _logger.WriteInfo(_parameters, "Finished deployment...");

            return Task.FromResult(_result);
        }

        private void OutputReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                if (e.Data.Contains("Total Time for Template"))
                {
                    _result.Status = StampyCommon.Models.JobResult.Passed;
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

        #region Helpers
        private List<string> AvailableDeploymentTemplates
        {
            get
            {
                if (_availableDeploymentTemplates == null)
                {
                    var deploymentTemplatesDirectory = Path.Combine(_parameters.BuildPath, @"hosting\Azure\RDTools\Deploy\Templates");
                    _availableDeploymentTemplates = Directory.GetFiles(deploymentTemplatesDirectory, "*.xml")
                        .Select(s => s.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault())
                        .ToList();
                }

                return _availableDeploymentTemplates;
            }
        }

        private string DeployConsolePath
        {
            get
            {
                return Path.Combine(_parameters.BuildPath, @"HostingPrivate\tools\DeployConsole.exe");
            }
        }

        private string _jobDirectory
        {
            get
            {
                var jobDirectory = Path.Combine(Environment.GetEnvironmentVariable("StampyJobResultsDirectoryPath"), _parameters.RequestId);
                if (!Directory.Exists(jobDirectory))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(jobDirectory));
                }
                return jobDirectory;
            }
        }

        private string _azureLogFilePath
        {
            get
            {
                var logFilePath = Path.Combine(_jobDirectory, "devdeploy", $"{_parameters.CloudName}.{_parameters.DeploymentTemplate.Replace(".xml", string.Empty)}.log");
                var logDirectory = Path.GetDirectoryName(logFilePath);
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
                return logFilePath;
            }
        }

        private string _deploymentArtificatsDirectory
        {
            get
            {
                return Path.Combine(Path.GetTempPath(), "DeployConsole", _parameters.CloudName);
            }
        }
        #endregion
    }
}
