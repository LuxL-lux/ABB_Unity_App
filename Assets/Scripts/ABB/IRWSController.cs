/****************************************************************************
RWS Controller Interface - Common interface for RWS controllers
****************************************************************************/

using System;

public interface IRWSController
{
    string IPAddress { get; }
    int Port { get; }
    string Username { get; }
    string Password { get; }
    string TaskName { get; }
    int PollingIntervalMs { get; }
    bool UseWebSocket { get; }
    bool ShowPerformanceMetrics { get; }
    
    void OnConnectionEstablished(bool usingWebSocket);
    void OnConnectionLost(string error = "");
    void OnDataReceived(double[] jointData);
    void OnConnectionStopped();
}