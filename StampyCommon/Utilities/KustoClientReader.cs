using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kusto.Data;
using Kusto.Data.Common;

namespace StampyCommon.Utilities
{
    public class KustoClientReader : IDisposable
    {
        ICslQueryProvider _kustoClient;

        public KustoClientReader(IConfiguration configuration, string database)
        {
            var kustoConnectionStringBuilderDM = new KustoConnectionStringBuilder(@"https://wawstest.kusto.windows.net:443")
            {
                FederatedSecurity = true,
                InitialCatalog = database,
                ApplicationKey = configuration.KustoClientSecret,
                ApplicationClientId = configuration.KustoClientId
            };

            _kustoClient = Kusto.Data.Net.Client.KustoClientFactory.CreateCslQueryProvider(kustoConnectionStringBuilderDM);
        }

        public async Task<IDataReader> GetData(string clientName, string database, string query)
        {
            return await _kustoClient.ExecuteQueryAsync(database, query, new ClientRequestProperties { Application = clientName });
        }

        public void Dispose()
        {
            _kustoClient.Dispose();
        }
    }
}
