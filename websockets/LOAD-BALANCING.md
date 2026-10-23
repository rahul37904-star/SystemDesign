# Load balancers: L4 and L7, HTTP APIs, WebSockets, and SSE

This guide explains what load balancers do, how Layer 4 and Layer 7 balancing
differ, and what those differences mean for REST-style HTTP APIs, WebSockets,
and Server-Sent Events (SSE). It uses Azure services as examples, but the
protocol concepts apply to other clouds and self-managed infrastructure too.

## 1. What a load balancer does

A load balancer gives clients a stable frontend address and distributes
connections or requests across a pool of backend instances. It usually also
checks backend health so that new traffic is not sent to an instance that is
unavailable.

```text
                    +--> backend A
client --> frontend +--> backend B
                    +--> backend C
```

Without a balancer, clients may need to know individual server addresses, and
one server can become a bottleneck or single point of failure. With a balancer,
instances can be added or removed behind the frontend. A balancer does not by
itself make an application highly available: the backends, their data, health
checks, deployment process, and failure handling must also be designed for
availability.

Load balancing is often provided by a **reverse proxy**. A reverse proxy accepts
client connections on behalf of servers and forwards traffic to them. Many
products combine proxying, load balancing, TLS termination, routing, security
filters, and health checks, so product names alone do not always tell you
which OSI layer they operate at.

## 2. Layer 4 versus Layer 7

The OSI model is a useful mental model, not a perfect description of every
modern implementation.

| | Layer 4 (L4) | Layer 7 (L7) |
|---|---|---|
| Sees | TCP or UDP connections/flows, addresses, and ports | HTTP requests and responses, including host, path, headers, and sometimes body |
| Typical decision | Select a backend for a network flow | Select a backend or action for an HTTP request |
| Understands URL paths? | No | Yes |
| Can route `/api` differently from `/chat`? | No | Yes |
| Can terminate HTTPS and read HTTP? | Usually no; TLS may pass through as opaque TCP | Yes, when configured to terminate TLS |
| Typical uses | TCP/UDP services, simple high-throughput distribution, TLS passthrough | HTTP websites and APIs, host/path routing, redirects, WAF, TLS offload |
| Example Azure service | Azure Load Balancer | Azure Application Gateway; Azure Front Door is a global edge HTTP(S) service |

### L4: balance network flows

An L4 balancer makes its decision using network-level information. For TCP, it
typically selects a backend when a connection is established and keeps that
connection mapped to that backend for the connection's lifetime. This mapping
is sometimes called **flow affinity** or a flow hash. The balancer does not
know that a TCP byte stream contains an HTTP request, a REST endpoint, or a
WebSocket message.

Consequences:

- It cannot route by hostname, URL path, HTTP method, cookie, or HTTP header.
- It cannot apply HTTP redirects or inspect HTTP payloads.
- It can distribute new connections, but it does not move an established TCP
  connection to another backend.
- Several HTTP requests on one reused TCP connection go to the same backend.
  HTTP keep-alive and HTTP/2 can therefore make the effective distribution
  coarser than "one request per backend choice."
- TLS can pass through without decryption. The backend then owns the
  certificate and TLS processing. The balancer still cannot inspect encrypted
  HTTP traffic.

L4 does not necessarily mean "no persistence." Flow affinity is intrinsic to a
TCP connection. Some L4 products also offer configurable source-IP affinity,
but this can be uneven when many users share a NAT address and does not
understand application sessions.

### L7: balance HTTP requests

An L7 HTTP proxy terminates or otherwise parses HTTP and can make routing
decisions from request information. For example:

```text
https://example.com/api/orders  --> API backend pool
https://example.com/images/...  --> static-content backend pool
https://chat.example.com/ws     --> WebSocket backend pool
```

It can commonly provide TLS termination, host/path routing, redirects, header
rewrites, authentication integration, WAF rules, and HTTP-level health probes.
With TLS termination, the proxy decrypts client traffic, then usually creates a
separate connection to the backend. This makes HTTP features possible, but
means the proxy is a security and availability boundary: configure both
frontend and backend TLS appropriately, and ensure the application trusts
forwarded client/protocol headers only from the proxy.

An L7 proxy can choose a backend per HTTP request, but that does not guarantee
that every request reaches a different server. Keep-alive pools, HTTP/2
multiplexing, affinity rules, and product behavior all affect distribution.
An L7 proxy also cannot safely rebalance an already-running long-lived stream
as if it were a fresh request.

## 3. HTTP, REST, and the protocols in this guide

**HTTP** is the application protocol. **REST** is an architectural style for
designing APIs; it is not a separate transport protocol or a special kind of
load balancer. A REST-style API usually uses HTTP methods, URLs, headers, and
status codes. An L4 balancer sees only its TCP/UDP connection, while an L7
balancer can apply HTTP routing and policy to each request.

**WebSocket** starts with an HTTP opening handshake. If accepted, the server
returns `101 Switching Protocols`, and the connection becomes a persistent,
full-duplex WebSocket connection. Both client and server can send messages
over it. `ws://` is unencrypted; `wss://` is WebSocket over TLS.

**SSE** (Server-Sent Events) is an HTTP response stream. The browser makes an
HTTP request and the server keeps the response open, sending event data in
text format (normally with `Content-Type: text/event-stream`). It is
server-to-client only; the client sends data back with separate HTTP requests.
Browsers can automatically reconnect, and SSE supports event IDs that help an
application resume after a disconnect.

## 4. How balancing behaves for each workload

### REST-style HTTP APIs

REST APIs are usually the simplest workload to scale behind a balancer:
requests are relatively short, and a stateless API can serve each request on
any healthy instance.

An L7 balancer is useful when you need URL/host routing, TLS termination,
request-level health checks, WAF, or API-specific policies. An L4 balancer can
also carry HTTP or HTTPS traffic, but it treats it as TCP flows and cannot
provide those HTTP-aware features.

For reliable horizontal scaling:

- Keep request handling stateless where practical. Put shared session or
  business state in an appropriate shared store, rather than relying on
  process memory on one instance.
- Use health probes that test whether an instance can actually serve the
  intended traffic. A process being alive is not always enough.
- Treat retries carefully. A network error does not prove that a request was
  not processed. Automatic retries of non-idempotent operations can duplicate
  side effects unless the API uses idempotency protection.
- Do not add sticky sessions as a substitute for shared state unless the
  application genuinely requires affinity and you accept its failover limits.

### WebSockets

WebSockets work through an L4 balancer because the upgraded connection remains
a TCP connection. L4 does not need to understand the WebSocket messages; it
only needs to preserve the flow between the client and the selected backend.

An L7 proxy can route the initial HTTP handshake by host or path, then proxy
the upgraded connection. It must support the WebSocket `Upgrade` handshake and
long-lived connections. After the upgrade, many L7 proxies pass WebSocket
frames through rather than interpreting application messages. Do not assume
that HTTP WAF inspection continues to inspect every WebSocket frame.

Important constraints:

- **A connection is pinned to one backend.** It does not migrate when the
  balancer's pool changes or another backend becomes healthier.
- **A failed backend breaks its active sockets.** Clients need reconnect and
  backoff logic. The application may need to restore subscriptions or other
  connection state after reconnecting.
- **Scaling out mostly helps new connections.** Existing connections remain
  where they are until they close. Long-lived clients can leave a new instance
  underused and older instances overloaded.
- **Plan for connection state.** If a message needs to reach a client connected
  to another instance, use a deliberate inter-instance mechanism such as a
  broker or shared pub/sub system. A load balancer does not broadcast messages
  between backends.
- **Tune idle timeouts and keepalives end-to-end.** A proxy or firewall may
  close an apparently idle connection. Heartbeats must occur often enough for
  the shortest relevant idle timeout, and both endpoints must handle closure.
- **Capacity is connection-based.** Track concurrent sockets, memory, file
  descriptors, per-instance limits, bandwidth, and proxy limits—not just
  requests per second.
- **Deployments need draining.** Stop sending new handshakes to an instance,
  allow a bounded drain period, then close remaining sockets in a controlled
  way. Clients still need to reconnect.

### Server-Sent Events

SSE is HTTP, so an L7 proxy can apply HTTP routing to the request. An L4
balancer can carry the underlying TCP connection, but cannot identify an SSE
route or inspect its HTTP response.

SSE is a long-lived response, so configure the whole path to support streaming:

- Avoid response buffering that waits for a large response before forwarding
  bytes. Ensure the application flushes events and test the real proxy path;
  behavior and controls differ by product.
- Review idle and maximum connection timeouts. Send periodic SSE comments or
  heartbeat events if needed to keep an otherwise quiet stream active.
- Check compression and other response middleware. They can buffer small
  writes and delay event delivery unless configured for streaming.
- A stream remains attached to one backend. On failure, the client reconnects
  to a backend selected for the new request; `Last-Event-ID` and application
  replay logic can help avoid gaps.
- Account for many concurrent open HTTP responses as connections consuming
  backend and proxy resources.

SSE is a useful fit for one-way updates where a browser needs server-to-client
events and can send commands separately over HTTP. WebSockets fit naturally
when both sides need to send frequent messages over one persistent connection.
Neither choice removes the need for reconnect, authorization, capacity
planning, and application-level recovery.

## 5. Quick selection guide

| Requirement | Likely fit | Important caveat |
|---|---|---|
| Distribute TCP/UDP connections to VMs with minimal protocol awareness | L4 balancer | No path/host routing or HTTP policy |
| Route HTTP requests by hostname/path and terminate TLS | L7 reverse proxy / application gateway | Adds a proxy hop and requires correct forwarded-header and TLS configuration |
| Stateless REST API across multiple instances | Either; L7 if HTTP features are needed | Design shared state, safe retries, health checks, and graceful rollout |
| Persistent, bidirectional WebSocket connections | L4 or WebSocket-capable L7 proxy | Active sockets stay on one backend and are lost on relevant failures |
| Browser server-to-client event stream | HTTP-capable L7 proxy or L4 TCP path | Verify streaming (no buffering), timeouts, reconnect, and replay behavior |
| Global entry point, edge routing, or acceleration | Global HTTP edge service | Check exact protocol support, limits, caching behavior, and timeout constraints |

The right choice depends on requirements, not just protocol names. Ask:

1. Do I need HTTP-aware routing, TLS termination, WAF, or request policies?
2. Are connections short-lived, or do they stay open for minutes or hours?
3. What does the product's idle timeout do, and can it be configured?
4. What happens to clients when a backend fails or is drained?
5. How does the application share state and recover missed events?
6. What are the per-instance and per-proxy concurrent-connection limits?

## 6. Azure service examples

These services overlap in broad purpose but are not interchangeable:

- **Azure Load Balancer** is an L4 service for TCP/UDP traffic to VM and VM
  scale set backends. It uses health probes and load-balancing rules; it does
  not route on HTTP paths or terminate TLS as an HTTP proxy.
- **Azure Application Gateway** is a regional L7 web traffic load balancer
  with HTTP(S) routing and optional WAF. Microsoft documents native WebSocket
  support. It needs healthy HTTP/HTTPS backend probes for WebSocket backends.
  For SSE, verify streaming behavior and effective timeouts in the chosen
  configuration rather than assuming a particular buffering policy.
- **Azure Front Door Standard/Premium** is a global edge HTTP(S) service and
  supports WebSockets. Microsoft's WebSocket guidance documents a five-minute
  idle timeout and a two-hour maximum connection duration, and says Front Door
  does not support SSE. Check current product documentation before designing
  around service limits, since limits and SKUs can change.

For the WebSocket echo sample in this repository, either an L4 path to the
server or an L7 gateway that supports WebSocket can carry the connection. An
L7 gateway is useful for demonstrating the HTTP `101` handshake and path
routing; after upgrade, the echo messages are WebSocket frames, not ordinary
REST requests. The current sample has one server instance and no reconnect,
authentication, or shared connection state, so adding a balancer does not
make it highly available or production-ready.

The shared Azure learning VM already has a service bound to public TCP 443.
Do not place this sample on that same public listener or change shared NSG
rules as part of a demo without checking the existing service and agreeing on
the impact. A local demo or SSH tunnel avoids exposing a new public port.

## 7. A practical demo sequence

Use this sequence to observe the concepts without needing multiple backends:

1. Run the sample server and client locally. Confirm the client sends one text
   message and receives `Echo: ...`.
2. Observe the opening HTTP handshake and `101 Switching Protocols` using a
   WebSocket-aware inspection tool or server/proxy logs. The sample's console
   output demonstrates messages after the upgrade; it is not itself an HTTP
   request/response for every echo.
3. Change the client to connect through a local reverse proxy or gateway with
   a WebSocket-capable route. Confirm the handshake and echo still work.
4. If testing multiple backends, give each instance a distinct identifier in
   its response. Open several separate client connections and observe new
   connections being distributed. Send multiple messages on one socket and
   observe that they stay on the same backend.
5. Stop one backend while a socket is connected. Observe that the existing
   socket is interrupted; then test client reconnect and a new backend
   selection. This demonstrates why a balancer cannot migrate an active
   WebSocket.
6. For an SSE comparison, add a separate HTTP endpoint that emits
   `text/event-stream`, flushes periodic events, and reconnects with event IDs.
   Test through the proxy to detect buffering, idle timeout, and replay issues.

The current echo client sends one message and exits, so it demonstrates a
single WebSocket lifecycle rather than sustained reconnects, load distribution,
or SSE. Those behaviors require additional sample code or a test harness.

## 8. Further reading

- [Azure Load Balancer overview](https://learn.microsoft.com/azure/load-balancer/load-balancer-overview)
- [Azure Application Gateway overview](https://learn.microsoft.com/azure/application-gateway/overview)
- [WebSocket support in Azure Application Gateway](https://learn.microsoft.com/azure/application-gateway/application-gateway-websocket)
- [WebSocket on Azure Front Door Standard/Premium](https://learn.microsoft.com/azure/frontdoor/standard-premium/websocket)
- [RFC 9110: HTTP Semantics](https://www.rfc-editor.org/rfc/rfc9110)
- [RFC 6455: The WebSocket Protocol](https://www.rfc-editor.org/rfc/rfc6455)
- [WHATWG: Server-Sent Events](https://html.spec.whatwg.org/multipage/server-sent-events.html)
