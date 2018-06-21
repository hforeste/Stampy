using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Ingest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StampyCommon.Loggers
{
    public abstract class Logger : ILogger
    {
        private static IConfiguration _configuration;
        private string _tableName;
        private string _url;
        private static string _databaseName;
        private ITableLogger _logger;

        private ITableLogger MyLogger
        {
            get
            {
                if (_logger == null)
                {
                    if (_configuration.IsProduction)
                    {
                        _logger = new KustoClientLogger(_databaseName, _tableName, KustoColumnMappings, _configuration.KustoClientId, _configuration.KustoClientSecret);
                    }
                    else
                    {
                        _logger = new CsvFileLogger(_tableName, KustoColumnMappings);
                    }
                }

                return _logger;
            }
        } 

        protected abstract List<KustoColumnMapping> KustoColumnMappings { get; }

        public Logger(IConfiguration configuration, string tableName, string url = "https://wawstest.kusto.windows.net:443", string databaseName = "wawskustotest")
        {
            _configuration = configuration;
            _tableName = tableName;
            _url = url;
            _databaseName = databaseName;
        }

        public virtual void WriteError(string message, Exception ex = null) { }
        public virtual void WriteError(string source, string message, Exception ex = null) { }
        public virtual void WriteInfo(string message) { }
        public virtual void WriteInfo(string source, string message) { }

        protected void WriteEvent(string message)
        {
            WriteKustoOfflineThread(message);
        }

        protected void WriteEvent(object arg1 = null, object arg2 = null, object arg3 = null, object arg4 = null, object arg5 = null, 
            object arg6 = null, object arg7 = null, object arg8 = null, object arg9 = null, object arg10 = null)
        {
            var args = new List<object> { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10 };
            var message = string.Join(",", args.Select(a => a != null ? string.Format("\"{0}\"", a) : a));
            WriteKustoOfflineThread(message);
        }

        private async Task<IKustoIngestionResult> WriteKustoAsync(string message)
        {
            await MyLogger.WriteRow(message);

            return null;
        }

        private void WriteKustoOfflineThread(string message)
        {
            MyLogger.WriteRow(message);
        }

        private static Stream GenerateStreamFromString(string s)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
    }
}
