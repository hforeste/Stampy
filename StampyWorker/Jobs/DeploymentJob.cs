﻿using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.File;
using StampyCommon;
using StampyCommon.Models;
using StampyWorker.Utilities;
using System;
using System.Collections.Concurrent;
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
        private ConcurrentQueue<string> _deploymentContent;
        private List<Task> _loggingTasks;

        public DeploymentJob(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs)
        {
            _logger = logger;
            _result = new JobResult();
            _parameters = cloudStampyArgs;
            _statusMessageBuilder = new StringBuilder();
            _deploymentContent = new ConcurrentQueue<string>();
            _loggingTasks = new List<Task>();
        }

        public Status JobStatus { get; set; }
        public string ReportUri { get; set; }

        public async Task<JobResult> Execute()
        {

            if (!AvailableDeploymentTemplates.Any(d => d.Equals(_parameters.DeploymentTemplate, StringComparison.CurrentCultureIgnoreCase)))
            {
                throw new ArgumentException("Deployment Template does not exist");
            }

            if (!File.Exists(DeployConsolePath))
            {
                throw new FileNotFoundException($"Cannot find {DeployConsolePath}");
            }

            _logger.WriteInfo(_parameters, "Starting deployment...");

            var processStartInfo = new ProcessStartInfo();
            processStartInfo.FileName = DeployConsolePath;
            processStartInfo.Arguments = $"/LockBox={_parameters.CloudName} /Template={_parameters.DeploymentTemplate} /BuildPath={_parameters.BuildPath + @"\Hosting"} /TempDir={_deploymentArtificatsDirectory} /AutoRetry=true /LogFile={_localFilePath}";
            processStartInfo.UseShellExecute = false;
            processStartInfo.WindowStyle = ProcessWindowStyle.Minimized;
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
            JobStatus = _result.JobStatus;
            _logger.WriteInfo(_parameters, "Finished deployment...");
            _result.Message = _statusMessageBuilder.ToString();

            while (_deploymentContent.Any())
            {
                await CreateLogIfNotExistAppendAsync();
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            return _result;
        }

        private void OutputReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _deploymentContent.Enqueue(e.Data);
                CreateLogIfNotExistAppendAsync().ConfigureAwait(false);

                if (JobStatus == default(Status))
                {
                    JobStatus = Status.InProgress;
                }

                if (e.Data.Contains("Total Time for Template"))
                {
                    JobStatus = _result.JobStatus = Status.Passed;
                } else if (e.Data.Contains("Total Time wasted"))
                {
                    JobStatus = _result.JobStatus = Status.Failed;
                }
            }
        }

        private void ErrorReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _deploymentContent.Enqueue(e.Data);
                CreateLogIfNotExistAppendAsync().ConfigureAwait(false);
                _statusMessageBuilder.AppendLine(e.Data);
                JobStatus = _result.JobStatus = Status.Failed;
            }
        }

        public Task<bool> Cancel()
        {
            return Task.FromResult(true);
        }

        private async Task CopyToAzureFileShareAsync(string localFilePath)
        {
            var storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("StampyStorageConnectionString"));
            var fileShareClient = storageAccount.CreateCloudFileClient();
            var fileShare = fileShareClient.ListShares("stampy-job-results").FirstOrDefault();
            var root = fileShare.GetRootDirectoryReference();
            var requestDirectory = root.GetDirectoryReference(_parameters.RequestId);

            var operationContext = new OperationContext();

            if (await requestDirectory.CreateIfNotExistsAsync(null, operationContext))
            {
                _logger.WriteInfo(_parameters, string.Format("Created new file share to host deployment logs at {0}. HttpResult: {1}", requestDirectory.Uri, operationContext.LastResult.HttpStatusCode));
            }

            var logFileName = Path.GetFileName(_localFilePath);
            var fileReference = requestDirectory.GetFileReference(logFileName);
            await fileReference.UploadFromFileAsync(_localFilePath, AccessCondition.GenerateEmptyCondition(), null, operationContext);
            _logger.WriteInfo(_parameters, string.Format("Copying local deployment log to azure file share. HttpResult: {0}", operationContext.LastResult.HttpStatusCode));
        }

        private async Task CreateLogIfNotExistAppendAsync()
        {
            if (_loggingTasks.All(t => t.IsCompleted) || !_loggingTasks.Any())
            {
                StringBuilder logBuilder = new StringBuilder();
                var sw = Stopwatch.StartNew();
                while (_deploymentContent.Any() && sw.Elapsed <= TimeSpan.FromSeconds(10))
                {
                    string log;
                    if (_deploymentContent.TryDequeue(out log))
                    {
                        logBuilder.AppendLine(log);
                    }
                }
                sw.Stop();

                if (!string.IsNullOrWhiteSpace(logBuilder.ToString()))
                {
                    _loggingTasks.Add(CreateIfNotExistAppendAsync(logBuilder.ToString()));
                }
            }
            else
            {
                await Task.WhenAll(_loggingTasks);
                _loggingTasks.Clear();
            }
        }

        private async Task CreateIfNotExistAppendAsync(string content)
        {
            try
            {
                var storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("StampyStorageConnectionString"));
                var fileShareClient = storageAccount.CreateCloudFileClient();
                var fileShare = fileShareClient.ListShares("stampy-job-results").FirstOrDefault();
                var root = fileShare.GetRootDirectoryReference();
                var requestDirectory = root.GetDirectoryReference(_parameters.RequestId);

                var operationContext = new OperationContext();

                if (await requestDirectory.CreateIfNotExistsAsync(null, operationContext))
                {
                    _logger.WriteInfo(_parameters, string.Format("Created new file share to host deployment logs at {0}. HttpResult:{1}", requestDirectory.Uri, operationContext.LastResult.HttpStatusCode));
                }

                var logFileName = Path.GetFileName(_localFilePath);
                var fileReference = requestDirectory.GetFileReference(logFileName);

                var buffer = Encoding.UTF8.GetBytes(content);

                if (!await fileReference.ExistsAsync())
                {
                    await fileReference.CreateAsync(0, null, null, operationContext);
                    _logger.WriteInfo(_parameters, $"Create deployment log file in azure. Location: {fileReference.Uri} HttpResult: {operationContext.LastResult.HttpStatusCode}");
                }

                await fileReference.ResizeAsync(fileReference.Properties.Length + buffer.Length, null, null, operationContext);
                if (operationContext.LastResult.HttpStatusCode != 200)
                {
                    _logger.WriteInfo(_parameters, $"Resize the azure file {fileReference.Uri} so to add new content. HttpResult: {operationContext.LastResult.HttpStatusCode}");
                }

                using (var fileStream = await fileReference.OpenWriteAsync(null, null, null, operationContext))
                {
                    fileStream.Seek(buffer.Length * -1, SeekOrigin.End);
                    await fileStream.WriteAsync(buffer, 0, buffer.Length);
                }

                if (string.IsNullOrWhiteSpace(ReportUri))
                {
                    SharedAccessFilePolicy sharedPolicy = new SharedAccessFilePolicy()
                    {
                        SharedAccessExpiryTime = DateTime.UtcNow.AddMonths(1),
                        Permissions = SharedAccessFilePermissions.Read
                    };

                    var permissions = await fileShare.GetPermissionsAsync(null, null, null);
                    permissions.SharedAccessPolicies.Clear();
                    permissions.SharedAccessPolicies.Add("read", sharedPolicy);

                    await fileShare.SetPermissionsAsync(permissions, null, null, null);

                    ReportUri = new Uri(fileReference.StorageUri.PrimaryUri.ToString() + fileReference.GetSharedAccessSignature(null, "read")).ToString();
                }

            }catch(Exception ex)
            {
                _logger.WriteError(_parameters, $"Failed to write deployment log to azure file", ex);
                throw;
            }
        }

        #region Helpers

        private async Task GenerateReportUri()
        {
            var expireTime = DateTime.UtcNow.AddMinutes(30);
            while (true)
            {
                try
                {
                    ReportUri = await ReportHelper.GetReport(Environment.GetEnvironmentVariable("StampyStorageConnectionString"), _parameters.RequestId, _parameters.CloudName);
                    break;
                }
                catch (Exception ex)
                {
                    if (DateTime.UtcNow >= expireTime)
                    {
                        throw new Exception("Could not get the report URL for this deployment job", ex);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30));
                        //ignore, keep retrying
                        _logger.WriteError(_parameters, "Failed to get the report URL. Will retry.", ex);
                    }
                }
            }
        }

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

        private string _localFilePath
        {
            get
            {
                var logFilePath = Path.Combine(Path.GetTempPath(), "devdeploy", $"{_parameters.CloudName}.{_parameters.DeploymentTemplate.Replace(".xml", string.Empty)}.log");
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
