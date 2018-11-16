using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StampyCommon;
using StampyCommon.Models;

namespace StampyWorker.Jobs
{
    public abstract class AntaresDeploymentBaseJob : IJob
    {
        protected CloudStampyParameters Parameters { get; set; }
        private ICloudStampyLogger _logger;
        private StringBuilder _statusMessageBuilder;
        private JobResult _result;

        protected abstract void PreExecute();
        protected abstract List<AntaresDeploymentTask> GetAntaresDeploymentTasks();
        protected abstract void PostExecute();

        public AntaresDeploymentBaseJob(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs)
        {
            _logger = logger;
            _result = new JobResult();
            Parameters = cloudStampyArgs;
            _statusMessageBuilder = new StringBuilder();
        }

        public Status JobStatus { get; set; }
        public string ReportUri { get; set; }

        public Task<bool> Cancel()
        {
            return Task.FromResult(true);
        }

        public async Task<JobResult> Execute()
        {
            PreExecute();

            if (!File.Exists(AntaresDeploymentExecutablePath))
            {
                var ex = new FileNotFoundException("Cannot find file", Path.GetFileName(AntaresDeploymentExecutablePath));
                _logger.WriteError(Parameters, "Cannot find file", ex);
                throw ex;
            }

            var processStartInfo = new ProcessStartInfo();
            processStartInfo.FileName = AntaresDeploymentExecutablePath;
            processStartInfo.UseShellExecute = false;
            processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processStartInfo.RedirectStandardError = true;
            processStartInfo.RedirectStandardOutput = true;

            var tasks = GetAntaresDeploymentTasks();

            foreach (var task in tasks)
            {
                _logger.WriteInfo(Parameters, $"Executing task {task.Name} - {task.Description}");
                processStartInfo.Arguments = task.AntaresDeploymentExcutableParameters;
                _logger.WriteInfo(Parameters, $"Start {processStartInfo.FileName} {processStartInfo.Arguments}");

                using (var p = Process.Start(processStartInfo))
                {
                    p.BeginErrorReadLine();
                    p.BeginOutputReadLine();
                    p.OutputDataReceived += new DataReceivedEventHandler(OutputReceived);
                    p.ErrorDataReceived += new DataReceivedEventHandler(ErrorReceived);
                    p.WaitForExit();
                }
            }

            PostExecute();

            _result.Message = _statusMessageBuilder.ToString();
            _result.JobStatus = JobStatus = _result.JobStatus == Status.None ? Status.Passed : _result.JobStatus;
            return await Task.FromResult(_result);
        }

        private void OutputReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                if (JobStatus == default(Status))
                {
                    JobStatus = Status.InProgress;
                }

                _logger.WriteInfo(Parameters, e.Data);
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

        private string AntaresDeploymentExecutablePath
        {
            get
            {
                return Path.Combine(Parameters.BuildPath, @"hosting\Azure\RDTools\Tools\Antares\AntaresDeployment.exe");
            }
        }

        public class AntaresDeploymentTask
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string AntaresDeploymentExcutableParameters { get; set; }
        }
    }
}
