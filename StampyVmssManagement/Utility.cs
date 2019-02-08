using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

namespace StampyVmssManagement
{
    internal class Utility
    {
        internal static async Task<string> GetServicePrincipalAccessToken(string clientId, string clientSecret)
        {
            const string resourceId = "https://management.core.windows.net/";
            const string authority = "https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47";
            var clientCreds = new ClientCredential(clientId, clientSecret);

            var authContext = new AuthenticationContext(authority, TokenCache.DefaultShared);
            var result = await authContext.AcquireTokenAsync(resourceId, clientCreds).ConfigureAwait(false);
            return result.AccessToken;
        }
    }
}
