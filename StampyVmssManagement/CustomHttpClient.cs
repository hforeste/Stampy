﻿using Microsoft.Azure.WebJobs.Host;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace StampyVmssManagement
{
    public sealed class CustomHttpClient : HttpClient
    {
        private TraceWriter _logger;

        public CustomHttpClient(TraceWriter logger):base()
        {
            this._logger = logger;
        }

        public CustomHttpClient(TraceWriter logger, HttpMessageHandler handler) : base(handler)
        {
            this._logger = logger;
        }

        public CustomHttpClient(TraceWriter logger, HttpMessageHandler handler, bool disposeHandler) : base(handler, disposeHandler)
        {
            this._logger = logger;
        }

        public async Task<HttpResponseMessage> SendAsync(string operationName, HttpRequestMessage request)
        {
            var sw = new Stopwatch();
            try
            {
                sw.Start();
                var response = await SendAsync(request);
                sw.Stop();
                _logger.Info($"OperationName: {operationName} StatusCode: {(int)response.StatusCode} {response.StatusCode} LatencyInMillseconds: {sw.ElapsedMilliseconds}ms");
                return response.EnsureSuccessStatusCode();
            }
            catch(Exception ex)
            {
                _logger.Error($"OperationName: {operationName} Exception: {ex.ToString()}");
                throw ex;
            }
        }
    }
}
