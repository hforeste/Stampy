using AutomationUnit.Client;
using AutomationUnit.DataModel;
using StampyCommon;
using StampyCommon.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyWorker.Jobs
{
    internal class LabMachineTestClient : ITestClient
    {
        ICloudStampyLogger _logger;
        CloudStampyParameters _args;

        public LabMachineTestClient(ICloudStampyLogger logger, CloudStampyParameters args)
        {
            _logger = logger;
            _args = args;
        }

        public async Task<JobResult> ExecuteTestsAsync(string[] tests)
        {
            string labMachineAddress;
            var result = new JobResult();

            //TODO iterate over categories and send job to lab machines

            var labAgentParameters = new Dictionary<string, string>
            {
                { "BuildPath", _args.BuildPath },
                { "GeomasterDefinitions", $@"\\AntaresDeployment\PublicLockBox\{_args.CloudName}geo\developer.definitions"},
                { "AzureStampDefinitions", $@"\\AntaresDeployment\PublicLockBox\{_args.CloudName}\developer.definitions"},
                { "TestCommonConfig", $@"\\AntaresDeployment\PublicLockBox\{_args.CloudName}\TestCommon.dll.config"},
                { "TestConfigName", $"{_args.CloudName}geo"}
            };

            var labMachineClient = JobClient.Link(@"http://stampy.lab.redant.selfhost.corp.microsoft.com:8090/api");

            var jobAsyncTask = Task.Run(() => labMachineClient.Create(labAgentParameters));

            _logger.WriteInfo(_args, "Submit test task to lab machines");

            Job labMachineJob = null;

            try
            {
                labMachineJob = await jobAsyncTask.ConfigureAwait(false);
                _logger.WriteInfo(_args, $"Job Type: Test, Job Id: {labMachineJob.Id}, Uri: {labMachineJob.Report}");
                if (labMachineJob != null)
                {
                    _logger.WriteInfo(_args, "Waiting for test task...");
                    //periodically check the status of the build task
                    var timeout = TimeSpan.FromMinutes(60);
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedTicks <= timeout.Ticks)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1));

                        _logger.WriteInfo(_args, "Get test status from agent machines");
                        var tmp = await Task.Run(() => labMachineClient.Get(labMachineJob.Id)).ConfigureAwait(false);
                        _logger.WriteInfo(_args, $"Agent test job Id: {tmp.Id} Status: {tmp.State} Uri: {tmp.Report}");
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
                    }

                    _logger.WriteInfo(_args, "Waiting for test task timed out");
                    result.JobStatus = Status.Failed;
                    result.Message = "Waiting for test task timed out";
                }
                else
                {
                    throw new Exception();
                }
            }
            catch (Exception ex)
            {
                _logger.WriteError(_args, "Failed to submit test task to lab machines", ex);
                result.JobStatus = Status.Failed;
                result.Message = "Failed to submit test task to lab machines";
            }

            return result;
        }
    }
}
