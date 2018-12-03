using System;
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
        private List<Task> _loggingTasks;
        private ICloudStampyLogger _kustoLogger;
        private CloudStampyParameters _parameters;
        private IConfiguration _configuration;

        public AzureFileLogger(IConfiguration configuration, CloudStampyParameters p, ICloudStampyLogger logger)
        {
            _loggingTasks = new List<Task>();
            _kustoLogger = logger;
            _parameters = p;
        }

        public string AzureFileUri { get; private set; }

        public async Task CreateLogIfNotExistAppendAsync(string path, string content)
        {

            if (_loggingTasks.All(t => t.IsCompleted) || !_loggingTasks.Any())
            {
                if (!string.IsNullOrWhiteSpace(content))
                {
                    _loggingTasks.Add(CreateIfNotExistAppendAsync(path, content));
                }
            }
            else
            {
                _kustoLogger.WriteInfo(_parameters, "Waiting on writing logs to azure file");
                await Task.WhenAll(_loggingTasks);
                _loggingTasks.Clear();
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

                if (string.IsNullOrWhiteSpace(AzureFileUri))
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

                    AzureFileUri = new Uri(fileReference.StorageUri.PrimaryUri.ToString() + fileReference.GetSharedAccessSignature(null, "read")).ToString();
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
