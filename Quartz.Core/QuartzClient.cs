using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;


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

    private async Task StartReceivingAsync(CancellationToken cancellationToken)
    {
        var stream = _tcpClient.GetStream();
        using var incompleteMessageBuffer = new MemoryStream();

        while (!cancellationToken.IsCancellationRequested)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                var bytesRead = await stream.ReadAsync(buffer, cancellationToken);

                if (bytesRead == 0)
                {
                    break; // Connection closed
                }

                if (bytesRead == 3)
                {
                    continue; // ignore keep-alive message
                }

                // Append the new data to the incomplete message buffer
                incompleteMessageBuffer.Write(buffer, 0, bytesRead);

                // Process messages from the buffer
                ProcessMessages(incompleteMessageBuffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        Console.WriteLine("Stopped receiving data");
    }

    private static void ProcessMessages(MemoryStream bufferStream)
    {
        bufferStream.Position = 0; // Reset position to start reading
        var buffer = bufferStream.ToArray();
        int start = 0;

        while (true)
        {
            // Find the start and end of a message
            int headerStart = Array.IndexOf(buffer, (byte)0x01, start);
            if (headerStart == -1) break;

            int headerEnd = Array.IndexOf(buffer, (byte)0x02, headerStart + 1);
            if (headerEnd == -1) break;

            int bodyEnd = Array.IndexOf(buffer, (byte)0x03, headerEnd + 1);
            if (bodyEnd == -1) break;

            // Extract and process the message
            var header = JsonSerializer.Deserialize<QuartzMessageHeader>(
                new ReadOnlySpan<byte>(buffer, headerStart + 1, headerEnd - headerStart - 1));

            var body = Encoding.UTF8.GetString(buffer, headerEnd + 1, bodyEnd - headerEnd - 1);

            Console.WriteLine($"Header: {header}, Body: {body}");

            // Move the start pointer past the processed message
            start = bodyEnd + 1;
        }

        // Keep any remaining data in the buffer
        var remainingData = buffer.AsSpan(start).ToArray();
        bufferStream.SetLength(0); // Clear the buffer
        bufferStream.Write(remainingData, 0, remainingData.Length);
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
