using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using StampyCommon;
using StampyCommon.Loggers;
using StampyCommon.Models;

namespace StampyWorker.Jobs.BuildJobs
{
    internal class OneBranchBuildClient : IBuildClient, IJob
    {
        private ICloudStampyLogger _logger;
        private CloudStampyParameters _args;
        private ProcessAction _buildAction;
        private AzureFileLogger _buildLogsWritter;
        private List<Task> _buildLogsWriterUnfinishedJobs;

        public OneBranchBuildClient(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs)
        {
            _logger = logger;
            _args = cloudStampyArgs;
            var buildScript = Path.Combine(Directory.GetParent(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName).FullName,"BuildRemote.cmd");
            _buildAction = new ProcessAction()
            {
                WorkingDirectory = Environment.GetEnvironmentVariable("AntaresMainRepoLocation"),
                ProgramPath = buildScript,
                Arguments = new List<ActionArgument>()
                {
                    new ActionArgument(cloudStampyArgs.GitBranchName)
                }
            };

            _buildLogsWritter = new AzureFileLogger(new LoggingConfiguration(), cloudStampyArgs, logger);
            _buildLogsWriterUnfinishedJobs = new List<Task>();
        }

        public Status JobStatus { get; set; }
        public string ReportUri { get { return _buildLogsWritter.AzureFileUri; } set { } }

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
            var jResult = await ExecuteBuild();
            await Task.WhenAll(_buildLogsWriterUnfinishedJobs);
            return jResult;
        }

        public async Task<JobResult> ExecuteBuild(string dpkPath = null)
        {
            var jobResult = new JobResult();

            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AntaresMainRepoLocation")))
            {
                throw new Exception("Failed to find AAPT/Antares/Websites folder in local filesystem");
            }

            var cleanAction = new ProcessAction()
            {
                WorkingDirectory = Environment.GetEnvironmentVariable("AntaresMainRepoLocation"),
                ProgramPath = @"C:\Program Files\Git\cmd\git.exe",
                Arguments = new List<ActionArgument>
                {
                    new ActionArgument("clean"),
                    new ActionArgument("-fdx")
                }
            };

            await ProcessInvoker.Start(cleanAction, (output, isFailures) =>
            {
                JobStatus = Status.InProgress;
                _buildLogsWriterUnfinishedJobs.Add(_buildLogsWritter.CreateLogIfNotExistAppendAsync("build-logs.txt", output));
            });

            var buildTask = ProcessInvoker.Start(_buildAction, (output, isFailure) =>
            {
                JobStatus = Status.InProgress;
                _buildLogsWriterUnfinishedJobs.Add(_buildLogsWritter.CreateLogIfNotExistAppendAsync("build-logs.txt", output));
            });

            var copyOverMsBuildLogsTask = Task.Run(async () =>
            {
                long offset = 0;
                long byteCount = 0;
                while (!buildTask.IsCompleted)
                {
                    var msBuildLogFile = Path.Combine(Environment.GetEnvironmentVariable("AntaresMainRepoLocation"), "msbuild.log");
                    if (File.Exists(msBuildLogFile))
                    {
                        string content;
                        using (FileStream msBuildFileStream = new FileStream(msBuildLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            //I noticed in the middle of build, the msbuild log rewrites from the top losing all previous data from the first build.
                            //It could be build -x64 rewritting the log file that build -x32 commited, hence why its size is 0 bytes again

                            if (byteCount > msBuildFileStream.Length)
                            {
                                //TEMPORARY reset offset to zero to read the new content written out by build -x64
                                offset = 0;
                            }

                            byteCount = msBuildFileStream.Length;

                            msBuildFileStream.Seek(offset, SeekOrigin.Begin);
                            var reader = new StreamReader(msBuildFileStream);
                            content = await reader.ReadToEndAsync();
                        }
                        offset += Encoding.UTF8.GetByteCount(content);
                        _buildLogsWriterUnfinishedJobs.Add(_buildLogsWritter.CreateLogIfNotExistAppendAsync("build-logs.txt", content));
                    }

                    await Task.Delay(5000);
                }
            });

            await Task.WhenAll(new Task[] { buildTask, copyOverMsBuildLogsTask });

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
