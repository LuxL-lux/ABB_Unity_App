/****************************************************************************
ABB RWS API Client - Implementation of RWS API client for ABB robots
****************************************************************************/

using System;
using System.Net;
using System.Net.Http;
using UnityEngine;

namespace ABB.RWS
{
    [Serializable]
    public class RWSConnectionSettings
    {
        [SerializeField] public string ipAddress = "127.0.0.1";
        [SerializeField] public int port = 80;
        [SerializeField] public string username = "Default User";
        [SerializeField] public string password = "robotics";
    }

    public class ABBRWSApiClient : IRWSApiClient
    {
        private readonly RWSConnectionSettings settings;
        private bool isConnected;

        public ABBRWSApiClient(RWSConnectionSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public string IPAddress => settings.ipAddress;
        public int Port => settings.port;
        public string Username => settings.username;
        public string Password => settings.password;
        public bool IsConnected => isConnected;

        public void SetConnectionState(bool connected)
        {
            isConnected = connected;
        }

        public HttpClient CreateHttpClient()
        {
            var cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler
            {
                Credentials = new NetworkCredential(settings.username, settings.password),
                Proxy = null,
                UseProxy = false,
                CookieContainer = cookieContainer
            };

            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US");
            client.Timeout = TimeSpan.FromSeconds(10);
            return client;
        }

        public string BuildUrl(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint))
                throw new ArgumentException("Endpoint cannot be null or empty", nameof(endpoint));

            if (!endpoint.StartsWith("/"))
                endpoint = "/" + endpoint;

            // Add JSON format parameter to force JSON response
            string separator = endpoint.Contains("?") ? "&" : "?";
            endpoint += $"{separator}json=1";

            return $"http://{settings.ipAddress}:{settings.port}{endpoint}";
        }

        public override string ToString()
        {
            return $"RWS Client: {settings.ipAddress}:{settings.port} (Connected: {isConnected})";
        }
    }
}