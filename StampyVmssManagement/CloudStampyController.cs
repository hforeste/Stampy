using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using StampyCommon;
using StampyCommon.Utilities;
using StampyVmssManagement.Models;

namespace StampyVmssManagement
{
    public static class CloudStampyController
    {
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
                response.Add(new Request { Id = row["RequestId"].ToString(), RequestTimeStamp = DateTime.Parse(row["TimeStamp"].ToString()), User = row["User"].ToString(), Branch = row["DpkPath"].ToString(), Client = row["Client"].ToString(), JobTypes = row["JobTypes"].ToString().Split(new char[]{'|'}), TestCategories = row["TestCategories"].ToString() });
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

            if (string.IsNullOrWhiteSpace(request.Branch))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Request body is missing name of branch.");
            }

            if (request.JobTypes == null || !request.JobTypes.Any())
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Request body is missing the specific job types. eg., Build, Deploy, and/or Test");
            }

            if (string.IsNullOrWhiteSpace(request.Client))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Request body is name of the client. This can also be added in the user agent");
            }

            if (!req.Headers.UserAgent.Any())
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "Missing user agent");
            }

            var userAgent = req.Headers.UserAgent.First().Product;

            if (string.IsNullOrWhiteSpace(request.Id))
            {
                request.Id = Guid.NewGuid().ToString();
            }

            return req.CreateResponse(HttpStatusCode.Accepted, new { request.Id });
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
    }
}
