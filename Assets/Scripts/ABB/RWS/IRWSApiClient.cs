/****************************************************************************
RWS API Client Interface - Common interface for Robot Web Services API access
****************************************************************************/

using System.Net.Http;

namespace ABB.RWS
{
    public interface IRWSApiClient
    {
        string IPAddress { get; }
        int Port { get; }
        string Username { get; }
        string Password { get; }
        bool IsConnected { get; }
        
        HttpClient CreateHttpClient();
        string BuildUrl(string endpoint);
    }
}