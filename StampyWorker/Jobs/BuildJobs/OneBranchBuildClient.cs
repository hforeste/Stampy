using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        private ProcessAction _buildAction;

        public OneBranchBuildClient(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs)
        {
            _logger = logger;
            _args = cloudStampyArgs;
            _buildAction = new ProcessAction
            {
                ProgramPath = Path.Combine(Environment.GetEnvironmentVariable("AntaresMainRepoLocation"), "build-corext.cmd")
            };
        }

        public Status JobStatus { get; set; }
        public string ReportUri { get; set; }

        public Task<bool> Cancel()
        {
            bool response;
            if (!(response = ProcessInvoker.TryCancel(_buildAction)))
            {
                //force cancel if can and throw exception
                ProcessInvoker.Cancel(_buildAction);
                response = true;
            }

            return Task.FromResult(response);
        }

        public async Task<JobResult> Execute()
        {
            JobStatus = Status.Queued;
            return await ExecuteBuild();
        }

        public async Task<JobResult> ExecuteBuild(string dpkPath = null)
        {
            var jobResult = new JobResult();

            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AntaresMainRepoLocation")))
            {
                throw new Exception("Failed to find AAPT/Antares/Websites folder in local filesystem");
            }

            await ProcessInvoker.Start(_buildAction, (output, isFailure) =>
            {
                JobStatus = Status.InProgress;
                if (isFailure)
                {
                    JobStatus = jobResult.JobStatus = Status.Failed;
                    jobResult.Message = $"Output fom stderr while running process => {output}";
                }
                Console.WriteLine(output);
            });

            //check to see if build failed
            string msBuildErrorFile = Path.Combine(Environment.GetEnvironmentVariable("AntaresMainRepoLocation"), "msbuild.err");
            if (File.Exists(msBuildErrorFile))
            {
                var buildErrors = new List<string>();
                using (var reader = new StreamReader(File.OpenRead(msBuildErrorFile)))
                {
                    while (!reader.EndOfStream)
                    {
                        buildErrors.Add(await reader.ReadLineAsync());
                    }
                }

                JobStatus = jobResult.JobStatus = Status.Failed;
                jobResult.Message = buildErrors.First();
                jobResult.ResultDetails["BuildErrors"] = string.Join(@"\r\n", buildErrors);
            }
            else
            {
                JobStatus = jobResult.JobStatus = Status.Passed;
            }

            return jobResult;
        }
    }
}
