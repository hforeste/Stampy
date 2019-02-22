using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using StampyCommon;
using StampyCommon.Loggers;
using StampyCommon.Utilities;
using StampyVmssManagement.Models;

namespace StampyVmssManagement
{
    public static class CloudStampyController
    {
        private static readonly CloudQueue buildQueue, deploymentQueue, testQueue;
        private static readonly StampyClientRequestLogger _logger;

        static CloudStampyController()
        {
            _logger = new StampyClientRequestLogger(new KustoConfiguration());
            // Retrieve storage account from connection string
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("CloudStampyStorageConnectionString"));

            // Create the queue client
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();

            // Retrieve a reference to a queue
            buildQueue = queueClient.GetQueueReference("build-jobs");
            deploymentQueue = queueClient.GetQueueReference("build-jobs");
            testQueue = queueClient.GetQueueReference("build-jobs");
        }

        [FunctionName("GetRequests")]
        public static async Task<HttpResponseMessage> ListRequests([HttpTrigger(AuthorizationLevel.Function, "get", Route = "requests")]HttpRequestMessage req, TraceWriter log)
        {
            var response = new List<Request>();
            var config = new KustoConfiguration();
            IDataReader reader;
            using (var client = new KustoClientReader(config, config.KustoDatabase))
            {
                reader = await client.GetData("stampyvmssmgmt", config.KustoDatabase, Queries.ListJobs);
            }

            var tableResult = new DataTable();
            tableResult.Load(reader);

            var columns = reader.GetSchemaTable().Columns;

            foreach (DataRow row in tableResult.Rows)
            {
                var jobs = row["JobTypes"].ToString().Split(new char[] { '|' }).ToList();
                var cloudServices = row["CloudServiceNames"]?.ToString().Split(new char[] { ',' });
                var deploymentTemplates = row["DeploymentTemplatePerCloudService"]?.ToString().Split(new char[] { ',' });
                Dictionary<string, string> cloudServiceAndDeploymentTemplate = null;
                if (cloudServices != null && deploymentTemplates != null)
                {
                    cloudServiceAndDeploymentTemplate = cloudServices.Zip(deploymentTemplates, (first, second) => new KeyValuePair<string, string>(first, second)).ToDictionary(kv => kv.Key, kv => kv.Value);
                }

                response.Add(new Request
                {
                    Id = row["RequestId"].ToString(),
                    RequestTimeStamp = DateTime.Parse(row["TimeStamp"].ToString()),
                    User = row["User"].ToString(), Branch = row["DpkPath"].ToString(),
                    Client = row["Client"].ToString(),
                    JobTypes = jobs,
                    TestCategories = row["TestCategories"].ToString(),
                    CloudDeployments = cloudServiceAndDeploymentTemplate
                });
            }

            return req.CreateResponse(HttpStatusCode.OK, response, "application/json");
        }

        [FunctionName("GetRequestDetails")]
        public static async Task<HttpResponseMessage> GetRequestDetails([HttpTrigger(AuthorizationLevel.Function, "get", Route = "requests/{id}")]HttpRequestMessage req, string id)
        {
            var response = new List<JobDetail>();
            var config = new KustoConfiguration();
            IDataReader reader;
            using (var client = new KustoClientReader(config, config.KustoDatabase))
            {
                reader = await client.GetData("stampyvmssmgmt", config.KustoDatabase, Queries.GetJobDetails(id));
            }

            var tableResult = new DataTable();
            tableResult.Load(reader);

            foreach (DataRow row in tableResult.Rows)
            {
                DateTime.TryParse(row["StartTime"].ToString(), out DateTime startTime);
                DateTime.TryParse(row["EndTime"].ToString(), out DateTime endTime);
                int.TryParse(row["JobDurationInMinutes"].ToString(), out int jobDurationInMinutes);
                response.Add(new JobDetail { Id = (string)row["JobId"], Type = (string)row["JobType"], Status = (string)row["Status"], StartTime = startTime, EndTime = endTime, ExceptionType = row["ExceptionType"].ToString(), ExceptionDetails = row["ExceptionDetails"].ToString(), JobDurationInMinutes = jobDurationInMinutes, ReportUri = new Uri(row["ReportUri"].ToString()) });
            }

            return req.CreateResponse(HttpStatusCode.OK, response, "application/json");
        }

        [FunctionName("QueueRequest")]
        public static async Task<HttpResponseMessage> QueueRequest([HttpTrigger(AuthorizationLevel.Function, "post", Route = "requests")]HttpRequestMessage req, TraceWriter log)
        {
            var requestBodyString = await req.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(requestBodyString))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Add request to body");
            }

            var request = JsonConvert.DeserializeObject<Request>(requestBodyString);

            if (request.JobTypes == null || !request.JobTypes.Any())
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Request body is missing the specific job types. eg., Build, Deploy, and/or Test");
            }

            StampyJobType jobTypes = StampyJobType.None;
            try
            {
                var jobTypeList = request.JobTypes.Select(t => (StampyJobType)Enum.Parse(typeof(StampyJobType), t));
                foreach (StampyJobType jobType in jobTypeList)
                {
                    jobTypes = jobTypes | jobType;
                }
            }
            catch (Exception)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, $"Incorrect job types. Please take a look. Eg., All the availabile job types are {Enum.GetNames(typeof(StampyJobType))}");
            }

            if ((jobTypes & StampyJobType.Build) == StampyJobType.Build)
            {
                if (string.IsNullOrWhiteSpace(request.Branch))
                {
                    return req.CreateResponse(HttpStatusCode.BadRequest, "Request body is missing name of branch.");
                }
            }

            if ((jobTypes & StampyJobType.CreateService) == StampyJobType.CreateService)
            {
                if (request.CloudDeployments == null || !request.CloudDeployments.Any())
                {
                    return req.CreateResponse(HttpStatusCode.BadRequest, "Request body is missing cloud service and deployment type");
                }
            }

            if ((jobTypes & StampyJobType.Build) == 0)
            {
                if (!string.IsNullOrWhiteSpace(request.BuildFileShare))
                {
                    var directories = Directory.GetDirectories(request.BuildFileShare);
                    if (!directories.Contains("debug-amd64", StringComparer.CurrentCultureIgnoreCase) && !directories.Contains("retail-amd64", StringComparer.CurrentCultureIgnoreCase))
                    {
                        return req.CreateResponse(HttpStatusCode.BadRequest, $"At the root of this file share, it does not contain one of the following: debug-amd64 or retail-amd64. Build File Share: {request.BuildFileShare}");
                    }
                }
            }

            if ((jobTypes & StampyJobType.Test) == StampyJobType.Test)
            {
                if (string.IsNullOrWhiteSpace(request.TestCategories))
                {
                    return req.CreateResponse(HttpStatusCode.BadRequest, "Request body is missing test categories.");
                }
            }

            if (!req.Headers.UserAgent.Any())
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Headers missing user-agent");
            }

            var userAgent = req.Headers.UserAgent.First().Product;

            if (string.IsNullOrWhiteSpace(request.Id))
            {
                request.Id = Guid.NewGuid().ToString();
            }

            var cloudStampyParameters = new CloudStampyParameters
            {
                RequestId = request.Id,
                JobType = jobTypes,
                TestCategories = request.TestCategories.Split(new char[] { ';' }).AsParallel().Select(s => s.Split(new char[] { ',' }).ToList()).ToList(),
                GitBranchName = request.Branch,
                CloudName = request.CloudDeployments != null && request.CloudDeployments.Any() ? request.CloudDeployments.First().Key : GetRandomStampName(),
                BuildPath = request.BuildFileShare,
                DpkPath = request.DpkPath,
                DeploymentTemplate = request.CloudDeployments != null && request.CloudDeployments.Any() ? request.CloudDeployments.First().Value : ""
            };

            var cloudQueueMessage = new CloudQueueMessage(cloudStampyParameters.ToJsonString());
            var cxt = new OperationContext();
            cxt.ClientRequestID = request.Id;

            Exception storageException = null;
            try
            {
                await buildQueue.AddMessageAsync(cloudQueueMessage, null, null, null, cxt);
            }catch(Exception ex)
            {
                storageException = ex;
            }
            finally
            {
                var requestForLogging = new StampyClientRequest
                {
                    BuildPath = cloudStampyParameters.BuildPath,
                    Client = userAgent.Name,
                    CloudNames = request.CloudDeployments?.Keys.ToList() ?? new List<string> { cloudStampyParameters.CloudName },
                    EndUserAlias = request.User,
                    RequestId = request.Id,
                    TestCategories = request.TestCategories.Split(new char[] { ';' }).ToList(),
                    DpkPath = request.DpkPath ?? request.Branch,
                    DeploymentTemplates = request.CloudDeployments?.Values.ToList(),
                    JobTypes = jobTypes
                };

                _logger.WriteRequest(requestForLogging);
            }

            if (cxt.LastResult.HttpStatusCode == (int)HttpStatusCode.Created || cxt.LastResult.HttpStatusCode == (int)HttpStatusCode.Accepted)
            {
                return req.CreateResponse(HttpStatusCode.Accepted, new { RequestId = request.Id, Message = $"Message was queued to cloud stampy storage account with ClientRequestId: {cxt.ClientRequestID} StatusCode: {cxt.LastResult.HttpStatusCode} Queue Message Id: {cloudQueueMessage.Id}" });
            }
            else
            {
                return req.CreateErrorResponse(HttpStatusCode.InternalServerError, (new { RequestId = request.Id, Message = $"Failed to queue request to cloud stampy storage account. ExceptionMessage: {storageException.Message}" }).ToString());                
            }
        }

        [FunctionName("CancelJob")]
        public static async Task<HttpResponseMessage> CancelJob([HttpTrigger(AuthorizationLevel.Function, "post", Route = "jobs/{id}")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            // parse query parameter
            string name = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "name", true) == 0)
                .Value;

            if (name == null)
            {
                // Get request body
                dynamic data = await req.Content.ReadAsAsync<object>();
                name = data?.name;
            }

            return name == null
                ? req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a name on the query string or in the request body")
                : req.CreateResponse(HttpStatusCode.OK, "Hello " + name);
        }

        private static string GetRandomStampName()
        {
            const string alphanumericalchars = "abcdefghijklmnopqrstuvwxyz1234567890";
            var rng = new RNGCryptoServiceProvider();
            byte[] xx = new byte[16];
            rng.GetBytes(xx);
            char[] y = new char[8];
            for (int i = 0; i < y.Length; i++)
            {
                y[i] = alphanumericalchars[(xx[i] % alphanumericalchars.Length)];
            }
            return $"stampy-{new string(y)}";
        }
    }
}
