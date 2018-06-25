using AutomationUnit.Client;
using AutomationUnit.DataModel;
using StampyCommon;
using StampyCommon.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace StampyWorker.Utilities
{
    internal class LabMachineBuildClient : IBuildClient
    {
        ICloudStampyLogger _logger;
        CloudStampyParameters _args;

        public LabMachineBuildClient(ICloudStampyLogger logger, CloudStampyParameters cloudStampArgs)
        {
            _logger = logger;
            _args = cloudStampArgs;
        }

        public async Task<JobResult> ExecuteBuild(string dpkPath)
        {
            var result = new JobResult();

            dpkPath = dpkPath ?? _args.DpkPath;

            var labMachineClient = JobClient.Link(@"http://stampy.lab.redant.selfhost.corp.microsoft.com:8080/api");
            var jobAsyncTask = Task.Run(() => labMachineClient.Create(new Dictionary<string, string> { { "Package", dpkPath } }));

            _logger.WriteInfo(_args, "Submit build task to lab machines");

            Job labMachineJob = null;

            try
            {
                labMachineJob = await jobAsyncTask.ConfigureAwait(false);
                if (labMachineJob != null)
                {
                    _logger.WriteInfo(_args, "Waiting for build task...");
                    //periodically check the status of the build task
                    var timeout = TimeSpan.FromMinutes(60);
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedTicks <= timeout.Ticks)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1));

                        _logger.WriteInfo(_args, "Get build status from agent machines");
                        var tmp = await Task.Run(() => labMachineClient.Get(labMachineJob.Id)).ConfigureAwait(false);

                        switch (tmp.State)
                        {
                            case JobState.Success:
                                result.JobStatus = Status.Passed;
                                break;
                            case JobState.Failed:
                                result.JobStatus = Status.Failed;
                                break;
                            case JobState.Aborting:
                            case JobState.Aborted:
                                result.JobStatus = Status.Cancelled;
                                break;
                            default:
                                break;
                        }

                        //when the job is done, get the new build path
                        if (result.JobStatus != default(Status))
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

                    _logger.WriteInfo(_args, "Waiting for build task timed out");

                    result.JobStatus = Status.Failed;
                    result.Message = "Waiting for build task timed out";
                }
                else
                {
                    throw new Exception();
                }
            }
            catch(Exception ex)
            {
                _logger.WriteError(_args, "Failed to submit build task to lab machines", ex);
                result.JobStatus = Status.Failed;
                result.Message = "Failed to submit build task to lab machines";
            }

            return result;
        }
    }
}
