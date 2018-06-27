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

        public async Task<JobResult> ExecuteTestAsync(string test)
        {
            var result = new JobResult();

            var labAddress = GetLabAddress(new List<string> { test });

            var labMachineClient = JobClient.Link(labAddress);

            var jobAsyncTask = Task.Run(() => labMachineClient.Create(GetLabMachineParameters(labAddress, test)));

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

        private string GetLabAddress(IEnumerable<string> tests)
        {
            if (tests.Any(s => s.Equals("ExpressFunctionals", StringComparison.CurrentCultureIgnoreCase)))
            {
                return LabAddress.ExpressFunctionalBase;
            }
            else if (tests.Any(s => s.Equals("ExpressStress", StringComparison.CurrentCultureIgnoreCase)))
            {
                return LabAddress.ExpressStressBase;
            }
            else
            {
                return LabAddress.TesterBase;
            }
        }

        private Dictionary<string, string> GetLabMachineParameters(string labMachineAddress, string testName)
        {
            var baseParameters = new Dictionary<string, string>
            {
                { "BuildPath", _args.BuildPath },
                { "GeomasterDefinitions", $@"\\AntaresDeployment\PublicLockBox\{_args.CloudName}geo\developer.definitions"},
                { "AzureStampDefinitions", $@"\\AntaresDeployment\PublicLockBox\{_args.CloudName}\developer.definitions"},
                { "TestCommonConfig", $@"\\AntaresDeployment\PublicLockBox\{_args.CloudName}geo\TestCommon.dll.config"},
                { "TestConfigName", $"{_args.CloudName}geo"}
            };

            switch (labMachineAddress)
            {
                case LabAddress.ExpressFunctionalBase:
                    return baseParameters;
                case LabAddress.TesterBase:
                    baseParameters.Add("Categories", ConvertToLabMachineTestCategory(testName));
                    AddAdditionalParameters(testName, baseParameters);
                    return baseParameters;
                default:
                    throw new Exception($"Cannot find parameter matching for lab adderss {labMachineAddress}");
            }
        }

        private string ConvertToLabMachineTestCategory(string testName)
        {

            var testNameMapping = new Dictionary<string, string>
            {
                { "Express", string.Empty},
                { ":-noexpress", string.Empty},
                { "CsmApiJune", "Api"},
                { "Mobile", string.Empty },
                { "CsmApi", "Api"},
                { "CsmBvt", "Bvt"}
            };

            foreach (var mapping in testNameMapping)
            {
                if (testName.ToLower().Contains(mapping.Key.ToLower()))
                {
                    testName = testName.Replace(mapping.Key.ToLower(), mapping.Value.ToLower());
                }
            }

            return testName;
        }

        private void AddAdditionalParameters(string testName, Dictionary<string, string> parameters)
        {
            var tmp = testName.ToLower();
            if (tmp.Contains(":-noexpress"))
            {
                parameters.Add("Additional", "-noexpress");
            }

            if (tmp.Contains("csmapijune"))
            {
                parameters["TestConfigName"] = parameters["TestConfigName"] + "-csm-june";
            }
            else if (tmp.Contains("mobile"))
            {
                parameters["TestConfigName"] = parameters["TestConfigName"] + "-mobile";
            }
            else if (tmp.Contains("csmapi") || tmp.Contains("csmbvt"))
            {
                parameters["TestConfigName"] = parameters["TestConfigName"] + "-csm";
            }
        }

        private class LabAddress
        {
            public const string BuilderBase = "http://stampy.lab.redant.selfhost.corp.microsoft.com:8080/api";
            public const string DebuggerBase = "http://stampy.lab.redant.selfhost.corp.microsoft.com:10000/api";
            public const string ExpressFunctionalBase = "http://stampy.lab.redant.selfhost.corp.microsoft.com:9020/api";
            public const string ExpressStressBase = "http://stampy.lab.redant.selfhost.corp.microsoft.com:9030/api";
            public const string OnPremDeployerBase = "http://stampy.lab.redant.selfhost.corp.microsoft.com:9000/api";
            public const string TesterBase = "http://stampy.lab.redant.selfhost.corp.microsoft.com:8090/api";
        }
    }
}
