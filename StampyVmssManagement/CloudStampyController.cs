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
        [FunctionName("GetJobs")]
        public static async Task<HttpResponseMessage> GetJobs([HttpTrigger(AuthorizationLevel.Function, "get", Route = "jobs")]HttpRequestMessage req, TraceWriter log)
        {
            var response = new List<object[]>();
            var config = new KustoConfiguration();
            IDataReader reader;
            using (var client = new KustoClientReader(config, config.KustoDatabase))
            {
                reader = await client.GetData("stampyvmssmgmt", config.KustoDatabase, Queries.ListJobs);
            }

            var columns = reader.GetSchemaTable().Columns;
            while (reader.Read())
            {
                object[] values = new object[reader.FieldCount];
                reader.GetValues(values);
                response.Add(values);
            }

            return req.CreateResponse(HttpStatusCode.OK, response.Select(r => new { RequestTimeStamp = r[0], RequestId = r[1], User = r[2]}), "application/json");
        }

        [FunctionName("GetJobDetails")]
        public static async Task<HttpResponseMessage> GetJobDetails([HttpTrigger(AuthorizationLevel.Function, "get", Route = "jobs/{id}")]HttpRequestMessage req, string id)
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
                response.Add(new JobDetail { Id = (string)row["Id"], Type = (string)row["Type"], Status = (string)row["Status"], StartTime = startTime, EndTime = endTime, ExceptionType = row["ExceptionType"].ToString(), ExceptionDetails = row["ExceptionDetails"].ToString(), JobDurationInMinutes = jobDurationInMinutes, ReportUri = new Uri(row["ReportUri"].ToString()) });
            }

            return req.CreateResponse(HttpStatusCode.OK, response, "application/json");
        }

        [FunctionName("QueueJob")]
        public static async Task<HttpResponseMessage> QueueJob([HttpTrigger(AuthorizationLevel.Function, "post", Route = "jobs")]HttpRequestMessage req, TraceWriter log)
        {
            var requestBodyString = await req.Content.ReadAsStringAsync();
            var cloudStampyParameters = JsonConvert.DeserializeObject<StampyClientRequest>(requestBodyString);

            if (string.IsNullOrWhiteSpace(cloudStampyParameters.RequestId))
            {
                cloudStampyParameters.RequestId = Guid.NewGuid().ToString();
            }

            return req.CreateResponse(HttpStatusCode.Accepted, new { JobId = cloudStampyParameters.RequestId });
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
