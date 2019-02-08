using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StampyVmssManagement
{
    public static class ResourceCleaner
    {
        private static TraceWriter _logger;
        private static string _subscription = Environment.GetEnvironmentVariable("CloudResourcesSubscriptionId");
        private static string _clientId = Environment.GetEnvironmentVariable("AAD_ClientId");
        private static string _clientSecret = Environment.GetEnvironmentVariable("AAD_ClientSecret");
        private static int _resourceMaxLivingTimeInHours = Int32.Parse(Environment.GetEnvironmentVariable("ResourceMaxLivingTimeInHours"));

        [FunctionName("ResourceCleaner")]
        public static async void Run([TimerTrigger("* */5 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            _logger = log;
            var t1 = DeleteStorageAccounts();
            var t2 = DeleteSqlServers();
            var t3 = DeleteCloudServices();
            var tasks = new Task[] { t1, t2, t3 };
            await Task.WhenAll(tasks);
            foreach (Task<ResourceCleanerOperation> t in tasks)
            {
                var cleanOperation = await t;
                _logger.Info(cleanOperation.ToString());
            }
        }

        public static async Task<ResourceCleanerOperation> DeleteSqlServers()
        {
            var cleanOperation = new ResourceCleanerOperation();
            cleanOperation.ResourceType = "SQL Server";
            var deleteSqlServer = new List<Uri>();

            var clientId = Environment.GetEnvironmentVariable("AAD_ClientId");
            var clientSecret = Environment.GetEnvironmentVariable("AAD_ClientSecret");

            using (var httpClient = new CustomHttpClient(_logger))
            {
                var accessToken = await Utility.GetServicePrincipalAccessToken(clientId, clientSecret);
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                //list sql servers in the subscription
                var subscription = Environment.GetEnvironmentVariable("CloudResourcesSubscriptionId");
                string requestUri = $"https://management.azure.com/subscriptions/{subscription}/providers/Microsoft.Sql/servers?api-version=2015-05-01-preview";
                var request = new HttpRequestMessage(HttpMethod.Get, new Uri(requestUri));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await httpClient.SendAsync("GetSqlServers", request);
                var result = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(result);
                var serverResourceUrls = json["value"].Select(i => (JObject)i).Select(i => i["id"].Value<string>()).Select(path => $"https://management.azure.com{path}");
                cleanOperation.Total = serverResourceUrls.Count();

                foreach (var url in serverResourceUrls)
                {
                    var getDatabasesUrl = $"{url}/databases?api-version=2017-10-01-preview";
                    request = new HttpRequestMessage(HttpMethod.Get, getDatabasesUrl);
                    response = await httpClient.SendAsync("GetDatabases", request);
                    result = await response.Content.ReadAsStringAsync();
                    json = JObject.Parse(result);

                    var hostingDbCreationDateString = json["value"].Select(i => (JObject)i).Where(o => o["name"].Value<string>() == "master").Single()["properties"]["creationDate"].Value<string>();

                    if ((DateTime.UtcNow - DateTime.Parse(hostingDbCreationDateString)) >= TimeSpan.FromHours(_resourceMaxLivingTimeInHours))
                    {
                        request = new HttpRequestMessage(HttpMethod.Delete, $"{url}?api-version=2015-05-01-preview");
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        response = await httpClient.SendAsync("DeleteSqlServers", request);
                        _logger.Info($"DELETE SqlServer: {url.Split(new char[] { '/' }).Last()} StatusCode: {response.StatusCode}");
                        if (response.IsSuccessStatusCode)
                        {
                            cleanOperation.TotalScavenged += 1;
                        }
                        else
                        {
                            cleanOperation.TotalResourceDeletionFailures += 1;
                        }
                    }
                }
            }

            return cleanOperation;
        }

        public static async Task<ResourceCleanerOperation> DeleteStorageAccounts()
        {
            var cleanOperation = new ResourceCleanerOperation();
            cleanOperation.ResourceType = "Storage Accounts";
            using (var httpClient = new CustomHttpClient(_logger))
            {
                var accessToken = await Utility.GetServicePrincipalAccessToken(_clientId, _clientSecret);
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                string requestUri = $"https://management.azure.com/subscriptions/{_subscription}/resourceGroups/Default-Storage-CentralUS/providers/Microsoft.ClassicStorage/storageAccounts?api-version=2016-04-01";
                var request = new HttpRequestMessage(HttpMethod.Get, new Uri(requestUri));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var response = await httpClient.SendAsync("GetStorageAccounts", request);
                var result = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(result);

                cleanOperation.Total = json["value"].Select(i => (JObject)i).Count();

                var expiredStorageAccounts = json["value"].Select(i => (JObject)i)
                    .Where(o => (DateTime.UtcNow - DateTime.Parse(o["properties"]["creationTime"].Value<string>())) >= TimeSpan.FromHours(_resourceMaxLivingTimeInHours))
                    .Select(i => i["id"].Value<string>());

                foreach (var resourceId in expiredStorageAccounts)
                {
                    response = await httpClient.SendAsync("DeleteStorageAccounts", new HttpRequestMessage(HttpMethod.Delete, $"https://management.azure.com{resourceId}?api-version=2016-04-01"));
                    _logger.Info($"DELETE storageaccount: {resourceId.Split(new char[] { '/' }).Last()} StatusCode: {response.StatusCode}");
                    if (response.IsSuccessStatusCode)
                    {
                        cleanOperation.TotalScavenged += 1;
                    }
                    else
                    {
                        cleanOperation.TotalResourceDeletionFailures += 1;
                    }
                }
            }

            return cleanOperation;
        }

        public static async Task<ResourceCleanerOperation> DeleteCloudServices()
        {
            var cleanOperation = new ResourceCleanerOperation() { ResourceType = "Cloud Services" };
            var deleteRequestIds = new List<string>();
            var handler = new HttpClientHandler();
            X509Certificate2 certificate = GetMyX509Certificate(Environment.GetEnvironmentVariable("RDFECertThumbprint"));
            if (certificate != null)
            {
                _logger.Info("Cert found");
            }
            handler.ClientCertificates.Add(certificate);
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
            {
                return true;
            };

            Stream responseXmlDocument;
            using (var httpClient = new CustomHttpClient(_logger, handler))
            {
                string requestUri = $"https://management.core.windows.net/{_subscription}/services/hostedservices";
                var request = new HttpRequestMessage(HttpMethod.Get, new Uri(requestUri));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
                request.Headers.Add("x-ms-version", "2012-03-01");
                var response = await httpClient.SendAsync("GetCloudServices", request);
                responseXmlDocument = await response.Content.ReadAsStreamAsync();

                var xmlDoc = new XmlDocument();
                xmlDoc.Load(responseXmlDocument);
                foreach (XmlNode item in xmlDoc.DocumentElement.ChildNodes)
                {
                    cleanOperation.Total += 1;
                    string cloudServiceName = item["ServiceName"].FirstChild.Value;
                    string dateLastModifiedString = item["HostedServiceProperties"]["DateLastModified"].FirstChild.Value;
                    DateTime dateLastModified = DateTime.Parse(dateLastModifiedString);

                    if (DateTime.UtcNow.Subtract(dateLastModified).TotalHours > _resourceMaxLivingTimeInHours)
                    {
                        requestUri = $"https://management.core.windows.net/{_subscription}/services/hostedservices/{cloudServiceName}?comp=media";
                        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, requestUri);
                        deleteRequest.Headers.Add("x-ms-version", "2013-08-01");
                        var deleteResponse = await httpClient.SendAsync(deleteRequest);
                        var operationRequestId = deleteResponse.Headers.GetValues("x-ms-request-id").First();
                        _logger.Info($"DELETE cloud service: {cloudServiceName} StatusCode:{deleteResponse.StatusCode} x-ms-requestid:{operationRequestId}");
                        if (deleteResponse.IsSuccessStatusCode)
                        {
                            cleanOperation.TotalScavenged += 1;
                        }
                        else
                        {
                            cleanOperation.TotalResourceDeletionFailures += 1;
                        }
                        deleteRequestIds.Add(operationRequestId);
                    }
                }

                foreach (string operationRequestId in deleteRequestIds)
                {
                    var checkOperation = $"https://management.core.windows.net/{_subscription}/operations/{operationRequestId}";
                    var checkOperationRequest = new HttpRequestMessage(HttpMethod.Get, requestUri);
                    checkOperationRequest.Headers.Add("x-ms-version", "2013-08-01");
                    checkOperationRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
                    var checkOperationResponse = await httpClient.SendAsync(checkOperationRequest);

                    if (checkOperationResponse.IsSuccessStatusCode)
                    {
                        //check the response body for the status of the asynchronous operation
                        xmlDoc = new XmlDocument();
                        xmlDoc.Load(await checkOperationResponse.Content.ReadAsStreamAsync());
                        _logger.Info($"x-ms-requestid:{operationRequestId} StatusCode:{xmlDoc.DocumentElement["HttpStatusCode"].FirstChild.Value} Details:{xmlDoc.DocumentElement["Error"]["Code"].FirstChild.Value} {xmlDoc.DocumentElement["Error"]["Message"].FirstChild.Value}");
                    }
                }
            }

            return cleanOperation;
        }

        public static async Task DeleteResourceGroups()
        {
            var clientId = Environment.GetEnvironmentVariable("AAD_ClientId");
            var clientSecret = Environment.GetEnvironmentVariable("AAD_ClientSecret");
            using (var httpClient = new CustomHttpClient(_logger))
            {
                var accessToken = await Utility.GetServicePrincipalAccessToken(clientId, clientSecret);

                var subscription = Environment.GetEnvironmentVariable("CloudResourcesSubscriptionId");
                string requestUri = $"https://management.azure.com/subscriptions/{subscription}/resourcegroups?api-version=2018-05-01";
                var request = new HttpRequestMessage(HttpMethod.Get, new Uri(requestUri));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var response = await httpClient.SendAsync("ListResourceGroups", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(result);
                    var resourceGroupNames = json["value"].Select(i => (JObject)i).Select(i => i.Value<string>("name")).Where(s => s.StartsWith("stampy"));
                }
            }

        }

        private static X509Certificate2 GetMyX509Certificate(string thumbprint)
        {
            X509Store certStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            X509Certificate2 cert = null;
            certStore.Open(OpenFlags.ReadOnly);

            try
            {
                X509Certificate2Collection certCollection = certStore.Certificates.Find(
                                       X509FindType.FindByThumbprint,
                                       thumbprint,
                                       false);
                // Get the first cert with the thumbprint
                if (certCollection.Count > 0)
                {
                    cert = certCollection[0];
                }
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                if (cert == null)
                {
                    _logger.Info("Did not find cert");
                }
                certStore.Close();
            }

            if (cert == null)
            {
                throw new Exception(string.Format("Certificate with thumbprint {0} could not be found", thumbprint));
            }

            return cert;
        }
    }
}
