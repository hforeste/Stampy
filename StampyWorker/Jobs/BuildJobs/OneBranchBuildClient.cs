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
        private CloudStampyParameters _cloudStampyArgs;
        private List<ProcessAction> _runningProcess;
        private AzureFileLogger _buildLogsWritter;
        private List<Task> _buildLogsWriterUnfinishedJobs;
        private string _reportUri;
        private const string BUILD_LOG = "build-corext.log";
        private const string MACHINE_LOG = "machine.log";
        private const string MSBUILD_LOG = "msbuild.log";

        public OneBranchBuildClient(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs)
        {
            _logger = logger;
            _cloudStampyArgs = cloudStampyArgs;
            _buildLogsWritter = new AzureFileLogger(new LoggingConfiguration(), cloudStampyArgs, logger);
            _buildLogsWriterUnfinishedJobs = new List<Task>();
            _runningProcess = new List<ProcessAction>();
        }

        public Status JobStatus { get; set; }
        public string ReportUri { get { return _reportUri; } set { } }

        public Task<bool> Cancel()
        {
            bool response = false;
            foreach (var item in _runningProcess)
            {
                if (!(response = ProcessInvoker.TryCancel(item)))
                {
                    //force cancel if can and throw exception
                    ProcessInvoker.Cancel(item);
                    response = true;
                }
            }
            return Task.FromResult(response);
        }

        public async Task<JobResult> Execute()
        {
            JobStatus = Status.Queued;
            JobResult jResult;
            try
            {
                jResult = await ExecuteBuild();
                JobStatus = jResult.JobStatus;

            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                await Task.WhenAll(_buildLogsWriterUnfinishedJobs);
            }

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
                Arguments = "clean -fdx"
            };

            var syncBranchScript = Path.Combine(Directory.GetParent(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName).FullName, "LocalSyncBranch.cmd");
            var syncBranchAction = new ProcessAction()
            {
                WorkingDirectory = Environment.GetEnvironmentVariable("AntaresMainRepoLocation"),
                ProgramPath = syncBranchScript,
                Arguments = $"{_cloudStampyArgs.GitBranchName}"
            };

            var buildAction = new ProcessAction()
            {
                WorkingDirectory = Environment.GetEnvironmentVariable("AntaresMainRepoLocation"),
                ProgramPath = Path.Combine(Environment.GetEnvironmentVariable("AntaresMainRepoLocation"), "build-corext.cmd")
            };

            var buildDirectoryName = $"{_cloudStampyArgs.GitBranchName.Replace(@"/", "-")}-{DateTime.UtcNow.ToString("yyyyMMddTHHmmss")}";
            var copyBuildAction = new ProcessAction()
            {
                WorkingDirectory = Environment.GetEnvironmentVariable("AntaresMainRepoLocation"),
                ProgramPath = @"C:\Windows\System32\Robocopy.exe",
                Arguments = $"out {Path.Combine(Environment.GetEnvironmentVariable("BuildVolume"), $"{buildDirectoryName}")} /MIR"
            };

            _logger.WriteInfo(_cloudStampyArgs, "Clean the working tree by recursively removing files that are not under version control, starting from the current directory.");
            int cleanActionExitCode = await ProcessInvoker.Start(cleanAction, (output, isFailures) =>
            {
                output = $"[{DateTime.UtcNow.ToString("HH:mm:ss")}]-" + output;
                JobStatus = Status.InProgress;
                _buildLogsWriterUnfinishedJobs.Add(_buildLogsWritter.CreateLogIfNotExistAppendAsync(MACHINE_LOG, output));
                _buildLogsWritter.LogUrls.TryGetValue(MACHINE_LOG, out _reportUri);
            });

            if (cleanActionExitCode != 0)
            {
                JobStatus = Status.Failed;
                throw new Exception($"Git clean failed");
            }

            _logger.WriteInfo(_cloudStampyArgs, $"Fetch the latest changes from {_cloudStampyArgs.GitBranchName} to the local file system");
            int syncBranchExitCode = await ProcessInvoker.Start(syncBranchAction, (output, isFailure) =>
            {
                output = $"[{DateTime.UtcNow.ToString("HH:mm:ss")}]-" + output;
                JobStatus = Status.InProgress;
                _buildLogsWriterUnfinishedJobs.Add(_buildLogsWritter.CreateLogIfNotExistAppendAsync(MACHINE_LOG, output));
                _buildLogsWritter.LogUrls.TryGetValue(MACHINE_LOG, out _reportUri);
            });

            if (syncBranchExitCode != 0)
            {
                JobStatus = Status.Failed;
                throw new Exception("Fetch latest changes failed");
            }

            var buildTask = ProcessInvoker.Start(buildAction, (output, ignore) =>
            {
                JobStatus = Status.InProgress;
                _buildLogsWriterUnfinishedJobs.Add(_buildLogsWritter.CreateLogIfNotExistAppendAsync(BUILD_LOG, output));
                _buildLogsWritter.LogUrls.TryGetValue(BUILD_LOG, out _reportUri);
            });

            var copyOverMsBuildLogsTask = Task.Run(async () =>
            {
                long offset = 0;
                long byteCount = 0;
                while (!buildTask.IsCompleted)
                {
                    var msBuildLogFile = Path.Combine(Environment.GetEnvironmentVariable("AntaresMainRepoLocation"), MSBUILD_LOG);
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
                        _buildLogsWriterUnfinishedJobs.Add(_buildLogsWritter.CreateLogIfNotExistAppendAsync(BUILD_LOG, content));
                    }

                    await Task.Delay(5000);
                }
            });

            _logger.WriteInfo(_cloudStampyArgs, "Start build");

            await Task.WhenAll(new Task[] { buildTask, copyOverMsBuildLogsTask });

            if (buildTask.Result != 0)
            {
                JobStatus = Status.Failed;
                throw new Exception("Build failed");
            }
            else
            {
                _logger.WriteInfo(_cloudStampyArgs, "Ran build successfully");
            }

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

                jobResult.JobStatus = Status.Failed;
                jobResult.ResultDetails["BuildErrors"] = string.Join(@"\r\n", buildErrors);
                return jobResult;
            }


            _logger.WriteInfo(_cloudStampyArgs, $"Copy build to {buildDirectoryName}");

            var buildOutputDirectory = Path.Combine(Environment.GetEnvironmentVariable("AntaresMainRepoLocation"), "out");
            if (!Directory.Exists(buildOutputDirectory))
            {
                throw new DirectoryNotFoundException($"Cant find directory {buildOutputDirectory}");
            }

            int copyBuildActionExitCode = await ProcessInvoker.Start(copyBuildAction, (output, isFailure) =>
            {
                JobStatus = Status.InProgress;
                _buildLogsWriterUnfinishedJobs.Add(_buildLogsWritter.CreateLogIfNotExistAppendAsync(MACHINE_LOG, output));
                _buildLogsWritter.LogUrls.TryGetValue(MACHINE_LOG, out _reportUri);
            });

            if (copyBuildActionExitCode > 1)//Robocopy error codes 0×01 = 1 = One or more files were copied successfully (that is, new files have arrived).
            {
                throw new Exception("Build could not be copied");
            }

            if (!Directory.Exists($@"\\{Environment.GetEnvironmentVariable("COMPUTERNAME")}\Builds"))
            {
                await ProcessInvoker.Start(@"C:\Windows\System32\net.exe", $@"share Builds={Environment.GetEnvironmentVariable("BuildVolume")}", null, (output, isFailed) =>
                {
                    JobStatus = Status.InProgress;
                    _buildLogsWriterUnfinishedJobs.Add(_buildLogsWritter.CreateLogIfNotExistAppendAsync(MACHINE_LOG, output));
                });
            }

            _buildLogsWritter.LogUrls.TryGetValue(BUILD_LOG, out _reportUri);
            jobResult.JobStatus = Status.Passed;
            jobResult.ResultDetails["Build Share"] = $@"\\{Environment.GetEnvironmentVariable("COMPUTERNAME")}\Builds\{buildDirectoryName}\debug-amd64";

            return jobResult;
        }
    }
}
