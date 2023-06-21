using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RtmpServer;

public class SocketServer : BackgroundService
{
    private readonly EndPoint _endPoint;

    public SocketServer(EndPoint endPoint)
    {
        _endPoint = endPoint;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using Socket client = new(
            _endPoint.AddressFamily,
            SocketType.Stream,
            ProtocolType.Tcp);

        await client.ConnectAsync(_endPoint, stoppingToken);
        while (stoppingToken.IsCancellationRequested == false)
        {
            // Send message.
            var message = "Hi friends 👋!<|EOM|>";
            var messageBytes = Encoding.UTF8.GetBytes(message);
            _ = await client.SendAsync(messageBytes, SocketFlags.None);
            Console.WriteLine($"Socket client sent message: \"{message}\"");

            // Receive ack.
            var buffer = new byte[1_024];
            var received = await client.ReceiveAsync(buffer, SocketFlags.None);
            var response = Encoding.UTF8.GetString(buffer, 0, received);
            if (response == "<|ACK|>")
            {
                Console.WriteLine(
                    $"Socket client received acknowledgment: \"{response}\"");
                break;
            }
            // Sample output:
            //     Socket client sent message: "Hi friends 👋!<|EOM|>"
            //     Socket client received acknowledgment: "<|ACK|>"
        }

        client.Shutdown(SocketShutdown.Both);

    }
}
