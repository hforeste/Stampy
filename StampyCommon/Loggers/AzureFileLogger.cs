using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.File;

namespace StampyCommon.Loggers
{
    public sealed class AzureFileLogger
    {
        private ConcurrentQueue<Task> _loggingTasks;
        private ICloudStampyLogger _kustoLogger;
        private CloudStampyParameters _parameters;
        private IConfiguration _configuration;
        private ConcurrentQueue<string> _requests;

        public AzureFileLogger(IConfiguration configuration, CloudStampyParameters p, ICloudStampyLogger logger)
        {
            _loggingTasks = new ConcurrentQueue<Task>();
            _kustoLogger = logger;
            _parameters = p;
            _configuration = configuration;
            _requests = new ConcurrentQueue<string>();
            LogUrls = new Dictionary<string, string>();
        }

        public string AzureFileUri { get; private set; }

        public Dictionary<string, string> LogUrls { get; private set; }

        public async Task CreateLogIfNotExistAppendAsync(string path, string content)
        {
            _requests.Enqueue(content);
            if (_loggingTasks.All(t => t.IsCompleted) || !_loggingTasks.Any())
            {
                StringBuilder logBuilder = new StringBuilder();
                while (_requests.Any())
                {
                    if (_requests.TryDequeue(out string log))
                    {
                        logBuilder.AppendLine(log);
                    }
                }

                if (!string.IsNullOrWhiteSpace(logBuilder.ToString()))
                {
                    _loggingTasks.Enqueue(CreateIfNotExistAppendAsync(path, logBuilder.ToString()));
                }
            }
            else
            {
                await Task.WhenAll(_loggingTasks);
                _loggingTasks.TryDequeue(out Task doneTask);
            }
        }

        private async Task CreateIfNotExistAppendAsync(string path, string content)
        {
            try
            {
                var storageAccount = CloudStorageAccount.Parse(_configuration.StorageAccountConnectionString);
                var fileShareClient = storageAccount.CreateCloudFileClient();
                var fileShare = fileShareClient.ListShares("stampy-job-results").FirstOrDefault();
                var root = fileShare.GetRootDirectoryReference();
                var requestDirectory = root.GetDirectoryReference(_parameters.RequestId);

                var operationContext = new OperationContext();

                if (await requestDirectory.CreateIfNotExistsAsync(null, operationContext))
                {
                    _kustoLogger.WriteInfo(_parameters, string.Format("Created new file share to host logs at {0}. HttpResult:{1}", requestDirectory.Uri, operationContext.LastResult.HttpStatusCode));
                }

                var logFileName = Path.GetFileName(path) ?? path;
                var fileReference = requestDirectory.GetFileReference(logFileName);

                var buffer = Encoding.UTF8.GetBytes(content);

                if (!await fileReference.ExistsAsync())
                {
                    await fileReference.CreateAsync(0, null, null, operationContext);
                    _kustoLogger.WriteInfo(_parameters, $"Create {logFileName} file in azure. Location: {fileReference.Uri} HttpResult: {operationContext.LastResult.HttpStatusCode}");
                }

                await fileReference.ResizeAsync(fileReference.Properties.Length + buffer.Length, null, null, operationContext);
                if (operationContext.LastResult.HttpStatusCode != 200)
                {
                    _kustoLogger.WriteInfo(_parameters, $"Resize the azure file {fileReference.Uri} so to add new content. HttpResult: {operationContext.LastResult.HttpStatusCode}");
                }

                using (var fileStream = await fileReference.OpenWriteAsync(null, null, null, operationContext))
                {
                    fileStream.Seek(buffer.Length * -1, SeekOrigin.End);
                    await fileStream.WriteAsync(buffer, 0, buffer.Length);
                }

                SharedAccessFilePolicy sharedPolicy = new SharedAccessFilePolicy()
                {
                    SharedAccessExpiryTime = DateTime.UtcNow.AddMonths(1),
                    Permissions = SharedAccessFilePermissions.Read
                };

                if (!LogUrls.ContainsKey(path))
                {
                    var permissions = await fileShare.GetPermissionsAsync(null, null, null);
                    permissions.SharedAccessPolicies.Clear();
                    permissions.SharedAccessPolicies.Add("read", sharedPolicy);

                    await fileShare.SetPermissionsAsync(permissions, null, null, null);

                    var azureFilesUri = new Uri(fileReference.StorageUri.PrimaryUri.ToString() + fileReference.GetSharedAccessSignature(null, "read")).ToString();
                    LogUrls.Add(path, azureFilesUri);
                }

            }
            catch (Exception ex)
            {
                _kustoLogger.WriteError(_parameters, $"Failed to write deployment log to azure file", ex);
                throw;
            }
        }
    }
}
