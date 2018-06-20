using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Ingest;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyCommon.Loggers
{
    internal class KustoClientLogger : ITableLogger
    {
        private IKustoIngestClient _kustoClient;
        private KustoIngestionProperties _kustoProperties;

        public KustoClientLogger(string databaseName, string tableName, List<KustoColumnMapping> _schema, string clientId, string clientKey)
        {
            var csvColumnMappings = new List<CsvColumnMapping>();

            foreach (var mapping in _schema)
            {
                csvColumnMappings.Add(new CsvColumnMapping { ColumnName = mapping.ColumnName, CslDataType = mapping.DataType, Ordinal = mapping.ColumnNumber });
            }

            _kustoProperties = new KustoIngestionProperties(databaseName, tableName)
            {
                Format = DataSourceFormat.csv,
                CSVMapping = csvColumnMappings
            };

            var kustoConnectionStringBuilderDM = new KustoConnectionStringBuilder(@"https://wawstest.kusto.windows.net:443")
            {
                FederatedSecurity = true,
                InitialCatalog = databaseName,
                ApplicationKey = clientKey,
                ApplicationClientId = clientId
            };

            _kustoClient = KustoIngestFactory.CreateDirectIngestClient(kustoConnectionStringBuilderDM);
        }

        public async Task WriteRow(string message)
        {
            await _kustoClient.IngestFromStreamAsync(GenerateStreamFromString(message), _kustoProperties).ConfigureAwait(false);
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
