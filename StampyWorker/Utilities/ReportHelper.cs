using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.File;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyWorker.Utilities
{
    internal class ReportHelper
    {
        public static async Task<string> GetReport(string storageConnectionString, string requestId, string cloudName, string fileShareName = "stampy-job-results")
        {
            string url = null;
            var storageAccount = CloudStorageAccount.Parse(storageConnectionString);
            var fileClient = storageAccount.CreateCloudFileClient();
            var fileShare = fileClient.GetShareReference(fileShareName);

            if (!await fileShare.ExistsAsync())
            {
                throw new Exception(string.Format("File share: {0} does not exist", fileShare.Name));
            }

            SharedAccessFilePolicy sharedPolicy = new SharedAccessFilePolicy()
            {
                SharedAccessExpiryTime = DateTime.UtcNow.AddMonths(1),
                Permissions = SharedAccessFilePermissions.Read
            };

            OperationContext cxt = null;
            var permissions = await fileShare.GetPermissionsAsync(null, null, cxt);
            permissions.SharedAccessPolicies.Clear();
            permissions.SharedAccessPolicies.Add("read", sharedPolicy);

            await fileShare.SetPermissionsAsync(permissions, null, null, cxt);

            var directory = fileShare.GetRootDirectoryReference();

            url = await GetDeployLogUrl(requestId, cloudName, directory);

            return url;
        }

        private static async Task<string> GetDeployLogUrl(string requestId, string cloudName, CloudFileDirectory directory)
        {
            CloudFile deploymentLog;

            var deployLogDirectory = directory.GetDirectoryReference(requestId).GetDirectoryReference("devdeploy");
            var files = deployLogDirectory.ListFilesAndDirectories(string.Format("{0}.", cloudName));

            if (!await deployLogDirectory.ExistsAsync())
            {
                throw new Exception(string.Format("Directory {0} does not exist in storage file share", deployLogDirectory.Name));
            }

            if (!files.Any())
            {
                return null;
            }

            if (files.Count() == 1)
            {
                deploymentLog = (CloudFile)files.Single();
            }
            else
            {
                throw new Exception(string.Format("Found more than one file/directory with name {0}", cloudName));
            }

            var uri = new Uri(deploymentLog.StorageUri.PrimaryUri.ToString() + deploymentLog.GetSharedAccessSignature(null, "read"));
            return uri.ToString();
        }
    }
}
