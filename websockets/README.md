# WebSockets: a basic C# example

This folder contains a small .NET 9 WebSocket echo demo:

- **WebSocketServer** is an ASP.NET Core server that accepts a WebSocket connection at `/ws` and echoes text messages.
- **WebSocketClient** is a console app that connects, sends one line of text, prints the reply, and closes the connection.

## Run the demo

The server launch profile listens on `https://localhost:61955` and
`http://localhost:61956`. This sample client uses the HTTP listener, so its
WebSocket URL is `ws://localhost:61956/ws`. (`wss://` is the secure WebSocket
scheme used with HTTPS.)

Open two PowerShell terminals in `C:\Users\rahul\Code\SystemDesign-upload`.

In terminal 1, start the server:

```powershell
dotnet run --project .\websockets\WebSocketServer\WebSocketServer.csproj
```

In terminal 2, start the client:

```powershell
dotnet run --project .\websockets\WebSocketClient\WebSocketClient.csproj
```

Type a message in the client. The server prints it and sends back `Echo: <your message>`. The client prints the reply and closes the connection.

## The opening handshake

WebSocket begins as an HTTP request. The client asks to switch protocols, for example:

```http
GET /ws HTTP/1.1
Host: localhost:61956
Upgrade: websocket
Connection: Upgrade
Sec-WebSocket-Key: <random base64 value>
Sec-WebSocket-Version: 13
```

`Sec-WebSocket-Key` is a fresh, random value for the handshake; it is not an authentication credential. `Sec-WebSocket-Version: 13` identifies the standardized WebSocket protocol version.

If the server accepts, it replies with **HTTP 101 Switching Protocols**, including:

```http
HTTP/1.1 101 Switching Protocols
Upgrade: websocket
Connection: Upgrade
Sec-WebSocket-Accept: <value derived from the client's key>
```

The server computes `Sec-WebSocket-Accept` from the client's key using the WebSocket-defined SHA-1 and Base64 procedure. The client checks this response. This confirms the protocol upgrade; it does **not** authenticate the user or encrypt traffic. Use `wss://` (WebSocket over TLS) outside a local learning demo, and implement application authentication and authorization separately where needed.

In this example, `ClientWebSocket.ConnectAsync` sends the HTTP upgrade request and validates the response. ASP.NET Core's `UseWebSockets` and `AcceptWebSocketAsync` handle the server side. Application code starts exchanging WebSocket messages only after the upgrade succeeds.

## What happens after the handshake?

The HTTP connection is upgraded and remains open as a **full-duplex** connection: client and server can both send messages without starting a new HTTP request for each one. The protocol carries data in frames; application code usually works with messages, which can consist of one or more frames.

- A message has a type, such as **text** (UTF-8) or **binary** (bytes). This sample supports text only.
- A message can be fragmented across multiple frames. `EndOfMessage` tells the receiver when all fragments for that message have arrived; the sample collects fragments before decoding or echoing.
- Control frames include **Ping**, **Pong**, and **Close**. Ping/Pong can help check that a connection is responsive. A Close frame starts the orderly shutdown; both sides should finish closing rather than just abandoning the connection.
- WebSocket is a transport, not an application message format. Real applications define their own message structure, validation, and error handling (for example JSON messages with a `type` field).

For production connections across the public internet, use `wss://`, authenticate and authorize requests, validate messages, and consider connection limits and timeouts.
