using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Xml;
using AutomationUnit.Client;
using AutomationUnit.DataModel;
using StampyCommon;
using StampyCommon.Models;
using StampyWorker.Jobs;

namespace StampyWorker.Utilities
{
    internal class LabMachineBuildClient : IBuildClient, IJob
    {
        ICloudStampyLogger _logger;
        CloudStampyParameters _args;

        public LabMachineBuildClient(ICloudStampyLogger logger, CloudStampyParameters cloudStampArgs)
        {
            _logger = logger;
            _args = cloudStampArgs;
        }

        public Status JobStatus { get; set; }
        public string ReportUri { get; set; }

        public Task<bool> Cancel()
        {
            throw new NotImplementedException();
        }

        public Task<JobResult> Execute()
        {
            throw new NotImplementedException();
        }

        public async Task<JobResult> ExecuteBuild(string dpkPath)
        {
            var result = new JobResult();

            dpkPath = dpkPath ?? _args.DpkPath;

            var labMachineClient = JobClient.Link(@"http://stampy.lab.redant.selfhost.corp.microsoft.com:8080/api");
            var jobAsyncTask = Task.Run(() => labMachineClient.Create(new Dictionary<string, string> { { "Package", dpkPath } }));

            _logger.WriteInfo(_args, "Submit build task to lab machines");

            var labMachineJob = await jobAsyncTask.ConfigureAwait(false);

            if (labMachineJob != null)
            {
                _logger.WriteInfo(_args, "Waiting for build task...");
                //periodically check the status of the build task
                var timeout = TimeSpan.FromMinutes(60);
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedTicks <= timeout.Ticks)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1));
                    var tmp = await Task.Run(() => labMachineClient.Get(labMachineJob.Id)).ConfigureAwait(false);

                    if (tmp == null)
                    {
                        throw new Exception($"Failed to get job id {labMachineJob.Id} from lab.");
                    }

                    switch (tmp.State)
                    {
                        case JobState.Success:
                            JobStatus = result.JobStatus = Status.Passed;
                            break;
                        case JobState.Failed:
                            JobStatus = result.JobStatus = Status.Failed;
                            break;
                        case JobState.Aborting:
                        case JobState.Aborted:
                            JobStatus = result.JobStatus = Status.Cancelled;
                            break;
                        case JobState.Running:
                            JobStatus = Status.InProgress;
                            break;
                        case JobState.Created:
                            JobStatus = Status.Queued;
                            break;
                        default:
                            break;
                    }

                    if (string.IsNullOrWhiteSpace(ReportUri))
                    {
                        ReportUri = labMachineJob.Report;
                    }

                    //when the job is done, get the new build path
                    if (result.JobStatus != default(Status) && result.JobStatus != Status.Queued && result.JobStatus != Status.InProgress)
                    {
                        _logger.WriteInfo(_args, "Getting build path from agent machines");
                        string xmlResult = labMachineClient.GetResult(labMachineJob.Id);
                        var doc = new XmlDocument();
                        doc.LoadXml(xmlResult);
                        XmlNodeList bss = doc.GetElementsByTagName("BuildPath");
                        string buildShare = bss[0].InnerText;
                        result.ResultDetails.Add("Build Share", buildShare);
                        return result;
                    }
                }

                throw new Exception("Waiting for build task timed out");
            }
            else
            {
                throw new Exception("Failed to submit build task to lab machines");
            }
        }

        public Task<JobResult> ExecuteBuild(CloudStampyParameters p)
        {
            throw new NotImplementedException();
        }
    }
}
