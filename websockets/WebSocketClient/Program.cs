using System.Net.WebSockets;
using System.Text;

using var socket = new ClientWebSocket();
var serverUri = new Uri("ws://localhost:61956/ws");

Console.WriteLine($"Connecting to {serverUri}...");
await socket.ConnectAsync(serverUri, CancellationToken.None);
Console.WriteLine("Connected. Type a message and press Enter:");

var text = Console.ReadLine();
if (string.IsNullOrEmpty(text))
{
    Console.WriteLine("No message entered; closing connection.");
    await socket.CloseAsync(
        WebSocketCloseStatus.NormalClosure,
        "No message entered.",
        CancellationToken.None);
    return;
}

var bytes = Encoding.UTF8.GetBytes(text);
await socket.SendAsync(
    new ArraySegment<byte>(bytes),
    WebSocketMessageType.Text,
    endOfMessage: true,
    CancellationToken.None);

using var reply = new MemoryStream();
var buffer = new byte[4096];
WebSocketReceiveResult result;

do
{
    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    if (result.MessageType == WebSocketMessageType.Close)
    {
        Console.WriteLine("Server closed the connection.");
        return;
    }

    reply.Write(buffer, 0, result.Count);
}
while (!result.EndOfMessage);

Console.WriteLine($"Server replied: {Encoding.UTF8.GetString(reply.ToArray())}");

await socket.CloseAsync(
    WebSocketCloseStatus.NormalClosure,
    "Demo complete.",
    CancellationToken.None);
Console.WriteLine("Connection closed.");
