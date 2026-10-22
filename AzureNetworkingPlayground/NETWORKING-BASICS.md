# Networking basics: from your PC to the Azure API

This guide introduces the networking ideas behind the learning playground in plain language. It starts with the basic concepts, then connects them to the real Azure resources. The current resource values are a snapshot; see [NETWORKING.md](NETWORKING.md) for the detailed configuration.

## 1. What does it mean for computers to communicate?

Imagine sending a parcel:

- The **address** says where it should go.
- The **service or recipient** says who should receive it at that address.
- The **network** carries it there.
- The **reply** has to find its way back.

Computer networking has similar pieces. An IP address helps identify where to send traffic. A port identifies the service on that computer. Networking equipment and software forward the traffic. A protocol such as TCP helps the two ends communicate reliably.

For this playground, your C# client sends a request to an API running in a container on an Azure virtual machine. The reply comes back over the connection.

## 2. IP addresses: where to send traffic

An **IP address** is a numeric network address used to deliver traffic to a destination. This playground uses IPv4 addresses, written as four numbers separated by dots.

There are two important kinds here:

- A **private IP address** is used inside a private network. The VM's current private IP is `10.20.1.4`. It identifies the VM on its Azure virtual network; a computer on the public internet cannot normally send traffic directly to that private address.
- A **public IP address** is reachable through the internet. The VM's Azure public IP is `20.204.42.165`. Internet clients use this public endpoint to reach the VM.

The public and private addresses serve different purposes. Think of the public IP as the building's street address and the private IP as an address used inside a private campus. Azure associates the public IP with the VM's network interface and delivers allowed incoming traffic to it.

The VM's private IP is dynamically allocated, so it may change if its network configuration is changed. The public IP resource is configured as static, so it is intended to stay the same while that resource is retained.

## 3. VNet, subnet, and CIDR: organizing private addresses

An **Azure Virtual Network (VNet)** is a private network boundary in Azure. It gives resources a private address space and a place to communicate privately, without using public IPs for every conversation. It also lets you divide systems into subnets and apply network rules that control which traffic is allowed. A VNet provides network connectivity and isolation; it does not automatically make every resource reachable, secure, or discoverable by application name.

This VNet is:

```text
vnet-netlab-ci-01
10.20.0.0/16
```

A **subnet** is a smaller address range inside a VNet. This playground's subnet is:

```text
snet-app-01
10.20.1.0/24
```

The `/16` and `/24` are **CIDR prefix lengths**. At a beginner level, they describe how large the corresponding IP address range is: a smaller prefix number represents a larger range. The subnet's range is contained within the VNet's range, and the VM's private IP `10.20.1.4` belongs to the subnet.

```text
VNet:     10.20.0.0/16
  Subnet: 10.20.1.0/24
    VM:   10.20.1.4
```

The PC running the client is not joined to this VNet. The public InternetDemo request reaches Azure through the VM's public IP instead.

### Why put services in a VNet?

If you have multiple services—for example, a web/API service and a database—you can place them in a VNet and let them communicate using private addresses. That traffic can stay on Azure's private network rather than exposing each service to the public internet. You can then allow only the required paths with NSGs and other controls; for example, allow the API to reach the database while not allowing internet clients to connect to the database directly.

So yes: a VNet and its subnets provide the private address space from which Azure assigns private IP addresses to network interfaces. A VNet alone does not guarantee that two services can communicate: routes, NSG rules, and a listening service must also permit the connection.

### If an IP is dynamic, how does an application find the service?

Applications generally should not rely on a dynamically allocated machine IP as a permanent identity. They can use a stable **name** and let DNS or a platform service-discovery system resolve that name to the current destination:

- In a small setup, configure a private DNS name such as `api.internal.example` and point it at the service's private IP. If the IP changes, update the DNS record or automate that update.
- For a group of replaceable service instances, use a stable frontend such as an internal load balancer, or a platform service name. The frontend/service discovery directs requests to the currently healthy instances.
- In Kubernetes, applications normally use a Service DNS name rather than a Pod IP. Pods can be recreated with different IPs, while the Service name remains stable.

There is no universal automatic mechanism just because a VNet exists; the chosen platform or DNS configuration provides discovery. For a single VM like this playground, Azure currently reports its NIC private IP as dynamically allocated. A normal guest-OS reboot usually leaves the NIC configuration alone, but a deallocation or network reconfiguration can result in a different dynamic private IP. Any other application that had hard-coded the old address would then need updating. A static private IP can be configured when a fixed address is genuinely needed, but stable DNS/service names are usually easier to operate as systems grow.

In this playground, the client on your PC does **not** use the VM's dynamic private IP. It calls the Azure DNS name, which resolves to the separate static public IP. No other microservices are currently deployed inside the VNet.

### Example: two VMs communicating privately

Suppose you add:

```text
API VM       10.20.1.4   listens on TCP 5000
Database VM  10.20.1.5   listens on TCP 5432
```

If both NICs are attached to subnets in the same VNet, the API VM can connect to the database VM's **private IP and port** (`10.20.1.5:5432`). Azure routes that traffic within the VNet; the database does not need a public IP for this communication. The database service must listen on an address reachable through its NIC, and its operating-system firewall and NSG must permit the connection.

For an experiment, the API could use the database's private IP directly. For a service that may move or whose address may change, give it a stable private DNS name (for example, `db.internal.example`) and have the API connect to that name. A private DNS zone can resolve the name to the current private address. For multiple interchangeable API instances, an internal load balancer can provide one stable private frontend and distribute connections to its backends.

Use NSG rules to limit which VM can reach which service and port. Be aware that Azure NSGs include a default inbound `AllowVnetInBound` rule, which permits traffic whose source and destination are considered part of the virtual network. Thus, simply placing VMs in the same VNet does not isolate them from one another. For tighter segmentation, add explicit allow rules for required paths and higher-priority deny rules for unwanted paths, or use suitable subnet/NSG segmentation; verify the effective rules and avoid accidentally blocking required traffic.

If the VMs are in **different VNets**, private communication requires connectivity between those networks, commonly VNet peering or a VPN, plus routes and security rules that allow the traffic. A VNet by itself does not connect to an unrelated VNet.

## 4. Ports and TCP: choosing the service

An IP address gets traffic to a computer or network interface; a **port** helps identify which service should receive it. Ports are numbered. A familiar convention is:

- TCP `443`: HTTPS web traffic.
- TCP `22`: SSH remote command-line access.

The client connects to the Azure endpoint on port `443`, where Kestrel serves the API over HTTPS. SSH uses port `22` to administer the VM. Port `5080` was used for the earlier plain-HTTP exercise; its public NSG rule has been removed.

**TCP** is a transport protocol used by both HTTPS and SSH here. It establishes a connection between the client and server and tracks the traffic so the two sides can exchange data. The client's connection also uses a temporary, or **ephemeral**, source port; the server destination port is `443` or `22`.

## 5. DNS: names instead of numbers

People find names easier to remember than IP addresses. **DNS** is the system that looks up a name and returns information such as the IP address to connect to. It is like looking up a business name in an address directory: the lookup tells the client where to go, but DNS does not carry the later API conversation.

The client uses this Azure DNS name:

```text
netlab-rahul-20261018.centralindia.cloudapp.azure.com
```

Its current DNS record resolves to:

```text
20.204.42.165
```

The HTTPS URL does not show `:443` because HTTPS clients use port `443` by default. The destination can therefore be written explicitly as:

```text
netlab-rahul-20261018.centralindia.cloudapp.azure.com:443
```

## 6. Routing: getting traffic there and back

**Routing** is the process of choosing where to forward network traffic so it can reach its destination. Your PC sends the request through its local network and internet provider toward Azure. Azure's network delivers traffic for the public IP to the Azure resource associated with that address.

Inside Azure, the VM's **network interface (NIC)** connects the VM to its subnet. The NIC has the private IP `10.20.1.4`, and its primary IP configuration is associated with the public IP resource. The VNet and subnet provide the private network context for the VM.

The response travels back over the established connection. The NSG is stateful, so it allows response traffic for a connection that it allowed; a separate inbound rule for the client's temporary source port is not needed.

This deployment has no custom route table, NAT Gateway, VPN, or load balancer. Azure uses its platform networking and system routes for this simple public endpoint.

## 7. What the Azure network components do

These components work together, but they are not interchangeable:

- **VNet:** the private Azure network and its address range.
- **Subnet:** a smaller range within the VNet where the VM NIC is placed.
- **NIC:** the VM's virtual network interface. It connects the VM to the subnet and has its private IP configuration.
- **Public IP resource:** the internet-facing address associated with the NIC's IP configuration.
- **NSG (Network Security Group):** a set of rules that allows or denies network traffic to or from an associated NIC or subnet. In this setup, it is associated with the NIC.
- **VM:** the computer running Ubuntu and Docker.
- **Docker port publishing:** forwards traffic from VM port `443` to container port `443`.
- **Kestrel:** the web server inside the API container. It accepts HTTPS, decrypts the TLS connection, and passes the HTTP request to the application.

An NSG is not the same thing as a router, a proxy, or a server. Allowing a port in the NSG does not start a service; the VM, Docker port mapping, and Kestrel listener must also be configured to accept the traffic.

## 8. NSG rules: which traffic is allowed?

An NSG evaluates rules against traffic. Rules can specify direction, protocol, source, destination, port, and whether to allow or deny. A rule with a lower priority number is evaluated before one with a higher number.

The important inbound rules for this VM are:

| Priority | Traffic | Source | Result |
|---:|---|---|---|
| 100 | TCP port `22` (SSH) | `49.37.129.153/32` | Allowed only from this configured public IPv4 address |
| 105 | TCP port `443` (HTTPS) | Any source | Allowed for the public API |
| Default | Other inbound traffic | Any source | Denied if no earlier rule allows it |

`/32` means one IPv4 address in CIDR notation. The SSH rule is intentionally limited to the configured address; if the PC's public IP changes, SSH may stop working until the rule is updated. HTTPS is open to any source for this study. The old public rule for TCP port `5080` is gone.

Azure also supplies default NSG rules. Inbound, these allow virtual-network traffic and Azure Load Balancer probes before denying other inbound traffic. The default load-balancer rule does not mean this setup has an Azure Load Balancer; there is no load balancer in this request path.

## 9. Follow an HTTPS request from your PC to the API

When you run the client with the `InternetDemo` profile, it calls the Azure DNS name. The request proceeds like this:

1. **DNS lookup:** The client looks up `netlab-rahul-20261018.centralindia.cloudapp.azure.com` and gets `20.204.42.165`.
2. **Connect:** The client opens a TCP connection to that public IP on port `443`. The source port on the client is temporary.
3. **Reach Azure:** The traffic travels from your PC's network through your internet provider and the public internet to Azure.
4. **Public IP association:** Azure maps traffic for `20.204.42.165` to the IP configuration on `nic-netlab-ci-01`.
5. **NSG evaluation:** `nsg-netlab-01` sees inbound TCP `443` and allows it because it matches the rule at priority `105`.
6. **VM and container:** The traffic is delivered to the VM. Docker publishes VM port `443` to container port `443`.
7. **HTTPS and API:** Kestrel in the container presents the study certificate and terminates TLS. It decrypts the HTTPS connection and passes the HTTP request to the ASP.NET Core API.
8. **Response:** The API's response returns over the connection, encrypted with HTTPS between the client and Kestrel.

In short:

```text
Client -> DNS lookup -> Internet -> Azure public IP -> NIC/NSG
       -> VM -> Docker port mapping -> Kestrel -> API
Client <- encrypted HTTPS response <----------------------+
```

The certificate is self-signed. This PC trusts it because its public certificate was explicitly added to the current Windows user's trusted root store. Other computers will not automatically trust it. HTTPS encrypts the connection, but this study setup should not be treated as a production certificate or production deployment.

## 10. Microsoft Entra sign-in is a separate conversation

The client first signs the user in with Microsoft Entra using the system browser. That browser/MSAL traffic goes to Microsoft over HTTPS. After receiving an access token, the C# client makes a separate HTTPS request to the Azure API and attaches the token as a bearer token.

```text
Browser / MSAL  -> Microsoft Entra (sign-in and token)
HelloClient     -> Azure API (request carrying the token)
```

The API checks the token and its required scope. DNS, TCP, and HTTPS are involved in both conversations, but their destinations are different.

## Development profile: localhost and SSH tunnel

The `Development` profile uses `https://localhost:5443` instead of the public DNS name. An SSH tunnel must be running:

```powershell
ssh.exe -i "$HOME\.ssh\id_ed25519_netlab_ci" -N -L 5443:127.0.0.1:443 azureuser@20.204.42.165
```

Here `localhost:5443` is a port on your PC. SSH carries that connection securely to `127.0.0.1:443` on the VM, then Docker forwards it to Kestrel. SSH itself connects to the VM's public IP on port `22`, which the NSG permits only from the configured source IP. TLS still terminates at Kestrel; the SSH tunnel does not replace HTTPS.

```text
HelloClient -> PC localhost:5443 -> SSH tunnel -> VM localhost:443
            -> Docker -> container Kestrel -> API
```

Use the InternetDemo profile for the public DNS route; use Development only while the tunnel is active.

## Quick glossary

| Term | Short meaning |
|---|---|
| IP address | Numeric network address used to deliver traffic |
| Private IP | Address used within a private network, such as the VM's `10.20.1.4` |
| Public IP | Internet-facing address, here `20.204.42.165` |
| VNet | Azure private network |
| Subnet | Smaller IP range inside a VNet |
| NIC | Virtual network interface connecting a VM to a subnet |
| Port | Number identifying a service on a computer |
| TCP | Transport protocol that establishes and tracks a connection |
| DNS | Lookup system that maps names to network information such as IP addresses |
| Route | A direction for forwarding traffic toward its destination |
| NSG | Azure network rules that allow or deny traffic |
| TLS | Encryption and server-identity protocol used by HTTPS |
| Kestrel | ASP.NET Core web server accepting the API's HTTP/HTTPS traffic |
