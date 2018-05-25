using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System.Data;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Text;
using System.Diagnostics;

namespace StampyVmssManagement
{
    public static class CapacityManager
    {
        private static TraceWriter _logger;
        private static string SubscriptionId { get { return Environment.GetEnvironmentVariable("Vmss_subscriptionId"); } }
        private static string ResourceGroup { get { return Environment.GetEnvironmentVariable("Vmss_resourceGroup"); } }
        private static string VmScaleSetName { get { return Environment.GetEnvironmentVariable("Vmss_resourceName"); } }

        private const int numBufferMachines = 10;

        [FunctionName("CapacityManager")]
        public static async Task ScaleVmss([TimerTrigger("*/30 * * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            _logger = log;
            var nDeploymentJobs = await GetNumberOfDeploymentJobs().ConfigureAwait(false);
            await ScaleStampyBackend(nDeploymentJobs);
        }

        /// <summary>
        /// Reset the virtual machine scale set by scaling into zero instances at specifically 2AM PST = 9AM UTC
        /// </summary>
        /// <param name="timer"></param>
        /// <returns></returns>
        [FunctionName("ResetScaleSet")]
        public static async Task ResetScaleSet([TimerTrigger("0 0 9 * * *")]TimerInfo timer, TraceWriter log)
        {
            log.Info("Reset virtual machine scale set");
            using (var httpClient = new CustomHttpClient(log))
            {
                var scaleRequest = FormScaleRequest(0);
                var token = await GetServicePrincipalAccessToken();
                scaleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var scaleResponse = await httpClient.SendAsync("UpdateCapacity", scaleRequest);
            }
        }

        #region Helpers
        private static async Task ScaleStampyBackend(int currentDeploymentRequests)
        {
            using (var httpClient = new CustomHttpClient(_logger))
            {
                var capacityRequest = FormCapacityStatusRequest();
                var token = await GetServicePrincipalAccessToken();
                bool scale = false;

                capacityRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await httpClient.SendAsync("GetInstanceSummary", capacityRequest);
                var responseText = await response.Content.ReadAsStringAsync();
                var capacityStatus = new CapacityStatus(responseText);

                _logger.Info($"ActiveWorkers: {capacityStatus.AvailableWorkers} ActiveJobs: {currentDeploymentRequests}");

                int numInstancesResult = capacityStatus.TotalInstances;

                if (currentDeploymentRequests + numBufferMachines > capacityStatus.TotalInstances)
                {
                    numInstancesResult = currentDeploymentRequests + numBufferMachines;
                    _logger.Info($"Scale out to {numInstancesResult} worker instances");
                    scale = true;
                }

                if (scale)
                {
                    var scaleRequest = FormScaleRequest(numInstancesResult);
                    scaleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    var scaleResponse = await httpClient.SendAsync("UpdateCapacity", scaleRequest);
                }
            }
        }

        private static HttpRequestMessage FormCapacityStatusRequest()
        {
            string requestUri = $"https://management.azure.com/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroup}/providers/Microsoft.Compute/VirtualMachineScaleSets/{VmScaleSetName}/instanceView?api-version=2017-03-30";
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(requestUri));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Host = "management.azure.com";
            return request;
        }

        private static async Task<string> GetServicePrincipalAccessToken()
        {
            var clientId = Environment.GetEnvironmentVariable("AAD_ClientId");
            var clientSecret = Environment.GetEnvironmentVariable("AAD_ClientSecret");
            const string resourceId = "https://management.core.windows.net/";
            const string authority = "https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47";
            var clientCreds = new ClientCredential(clientId, clientSecret);

            var authContext = new AuthenticationContext(authority, TokenCache.DefaultShared);
            var result = await authContext.AcquireTokenAsync(resourceId, clientCreds).ConfigureAwait(false);
            return result.AccessToken;
        }

        public static async Task<DataTable> GetDataFromQuery(string query, bool throwIfFailed = false)
        {
            var stampyDbConnectionString = Environment.GetEnvironmentVariable("StampyDbConnectionString");

            var adapter = new SqlDataAdapter(query, stampyDbConnectionString);
            int tries = 0;
            bool done = false;
            var data = new DataTable();
            while (!done)
            {
                try
                {
                    adapter.Fill(data);
                    done = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    Console.WriteLine(ex.StackTrace);
                    await Task.Delay(1000);
                    tries++;
                    done = (tries >= 3);
                    if (done && throwIfFailed)
                    {
                        throw;
                    }
                }
            }
            return data;
        }

        private static async Task<int> GetNumberOfDeploymentJobs()
        {
            const string query = @"
select *
from dbo.Flow as flow
join dbo.Activity as act
on act.flow_id = flow.id
where flow.CreatedTime >= DATEADD(day, -7, GETDATE()) 
and act.[state] in (0, 1, 2) 
and act.[type] LIKE '%Deploy%' 
and flow.id not in (select act2.flow_id from dbo.Activity as act2 where act2.flow_id = flow.id and act2.[state] in (4, 5, 6))";
            var data = await GetDataFromQuery(query).ConfigureAwait(false);
            var count = data.Rows.Count;
            return count;
        }

        public static HttpRequestMessage FormScaleRequest(int capacity)
        {
            string requestUri = $"https://management.azure.com/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroup}/providers/Microsoft.Compute/VirtualMachineScaleSets/{VmScaleSetName}?api-version=2017-03-30";
            var request = new HttpRequestMessage(HttpMethod.Put, new Uri(requestUri));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Host = "management.azure.com";
            request.Content = new StringContent(ScaleOperationToJson(capacity), ASCIIEncoding.UTF8, "application/json");
            return request;
        }

        private static string ScaleOperationToJson(int capacity)
        {
            var sku = new Sku { name = "Standard_D1_v2", tier = "Standard", capacity = capacity };
            var vmss = new Vmss { sku = sku, location = "westus" };
            string json = JsonConvert.SerializeObject(vmss);
            return json;
        }

        private class Vmss
        {
            public string location { get; set; }
            public Sku sku { get; set; }
        }

        private class Sku
        {
            public string name { set; get; }
            public string tier { set; get; }
            public int capacity { set; get; }
        }
        #endregion
    }
}
