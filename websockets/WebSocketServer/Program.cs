using System.Net.WebSockets;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.Map("/ws", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Connect using a WebSocket client.");
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    Console.WriteLine("Client connected.");

    var buffer = new byte[4096];
    while (socket.State == WebSocketState.Open)
    {
        using var message = new MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(
                    result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                    result.CloseStatusDescription,
                    CancellationToken.None);
                Console.WriteLine("Client disconnected.");
                return;
            }

            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        if (result.MessageType != WebSocketMessageType.Text)
        {
            await socket.CloseAsync(
                WebSocketCloseStatus.InvalidMessageType,
                "This sample accepts text messages only.",
                CancellationToken.None);
            return;
        }

        var text = System.Text.Encoding.UTF8.GetString(message.ToArray());
        Console.WriteLine($"Received: {text}");

        var reply = System.Text.Encoding.UTF8.GetBytes($"Echo: {text}");
        await socket.SendAsync(
            new ArraySegment<byte>(reply),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);
    }
});

app.Run();
