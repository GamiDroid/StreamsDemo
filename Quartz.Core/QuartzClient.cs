using System.IO;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Buffers;

namespace Quartz.Core;

public class QuartzClient(string ipAddress, int port) : IDisposable
{
    private readonly IPAddress _address = IPAddress.Parse(ipAddress);
    private readonly int _port = port;

    private readonly TcpClient _tcpClient = new();

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private Task? _dataReceivingTask;
    private Task? _keepAliveMonitorTask;

    public void Connect()
    {
        _tcpClient.Connect(_address, _port);

        _dataReceivingTask = StartReceivingAsync(_cancellationTokenSource.Token);
        _keepAliveMonitorTask = KeepAliveMonitor(_cancellationTokenSource.Token);
    }

    ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private async Task StartReceivingAsync(CancellationToken cancellationToken)
    {
        var stream = _tcpClient.GetStream();

        while (!cancellationToken.IsCancellationRequested)
        {
            var buffer = _arrayPool.Rent(4096);
            try
            {
                var bytesRead = await stream.ReadAsync(buffer, cancellationToken);

                if (bytesRead == 0)
                {
                    break; // Connection closed
                }

                if (bytesRead == 3)
                {
                    Console.WriteLine("KeepAlive received");
                    continue;
                }

                // Message is structured as: 0x01 + header + 0x02 + body + 0x03
                // Find the start and end of the message
                int headerStart = Array.IndexOf(buffer, (byte)0x01);
                if (headerStart == -1)
                {
                    Console.WriteLine("Invalid message format");
                }

                if (headerStart != 0)
                    Console.WriteLine("WARN Header start not at beginning of stream");

                int headerEnd = Array.IndexOf(buffer, (byte)0x02, headerStart + 1, bytesRead);
                int bodyStart = headerEnd + 1;
                int bodyEnd = Array.IndexOf(buffer, (byte)0x03, bodyStart, bytesRead - bodyStart);
                if (headerEnd == -1 || bodyEnd == -1)
                {
                    Console.WriteLine("Invalid message format");
                    continue;
                }

                // Extract header and body
                var header = Encoding.UTF8.GetString(buffer, headerStart + 1, headerEnd - headerStart - 1);
                var body = Encoding.UTF8.GetString(buffer, bodyStart, bodyEnd - bodyStart);

                // Process the received data
                var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"Received: {message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                // Return the buffer to the pool
                _arrayPool.Return(buffer);
            }
        }

        Console.WriteLine("Stopped receiving data");
    }

    private async Task KeepAliveMonitor(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SendInternalAsync(string.Empty, string.Empty, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                await Task.Delay(5_000);
            }
        }
    }

    public void Disconnect()
    {
        _cancellationTokenSource.Cancel();
        _tcpClient.Close();
    }

    public Task<bool> SendAsync(QuartzMessageHeader header, object body, CancellationToken cancellationToken)
    {
        var bodyAsJson = JsonSerializer.Serialize(body);
        return SendAsync(header, bodyAsJson, cancellationToken);
    }

    public Task<bool> SendAsync(QuartzMessageHeader header, string body, CancellationToken cancellationToken)
    {
        var headerAsJson = JsonSerializer.Serialize(header);
        return SendInternalAsync(headerAsJson, body, cancellationToken);
    }

    private async Task<bool> SendInternalAsync(string header, string body, CancellationToken cancellationToken)
    {
        if (!_tcpClient.Connected)
        {
            return false;
        }

        try
        {
            var stream = _tcpClient.GetStream();

            var send = new byte[header.Length + body.Length + 3];

            send[0] = 0x01;
            var idx = 1 + Encoding.UTF8.GetBytes(header, 0, header.Length, send, 1);
            send[idx++] = 0x02;
            idx += Encoding.UTF8.GetBytes(body, 0, body.Length, send, idx);
            send[idx++] = 0x03;

            await stream.WriteAsync(send, cancellationToken);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    #region Dispose Methods
    private bool _isDisposed;
    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
                _tcpClient.Close();
                _tcpClient.Dispose();

                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();

                _dataReceivingTask?.Dispose();
                _keepAliveMonitorTask?.Dispose();
                _dataReceivingTask = null;
                _keepAliveMonitorTask = null;
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _isDisposed = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    } 
    #endregion
}
