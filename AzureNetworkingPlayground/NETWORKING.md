# Networking configuration and request path

This document describes the Azure network carrying requests from the C# client on this PC to the learning API. It records the live configuration checked on **October 18, 2026**. Azure resource settings can change; the values below are a snapshot.

For plain-language explanations of the networking fundamentals used here, see [NETWORKING-BASICS.md](NETWORKING-BASICS.md).
For a relationship diagram showing how the VM, NIC, IP configuration, public IP, subnet, VNet, and NSG are associated, see [AZURE-RESOURCE-ASSOCIATIONS.md](AZURE-RESOURCE-ASSOCIATIONS.md).

## Network at a glance

```text
Your PC
  |
  | HTTPS to netlab-rahul-20261018.centralindia.cloudapp.azure.com:443
  | across your home/office network, ISP, and the public internet
  v
Azure public IP 20.204.42.165
  |
  | associated with the VM's NIC
  v
NIC nic-netlab-ci-01
  |  NSG nsg-netlab-01 filters inbound traffic
  |  private IPv4 10.20.1.4
  v
Subnet snet-app-01 (10.20.1.0/24)
  | inside
VNet vnet-netlab-ci-01 (10.20.0.0/16)
  |
  v
VM vm-netlab-ci-01
  |
  | Docker publishes host TCP 443 to container TCP 443
  v
API container: Kestrel terminates TLS and serves HTTPS
```

The arrows are the logical path, not a promise that packets travel through separate physical devices for each Azure resource. The NIC, VNet, subnet, public IP, and NSG are Azure networking resources/configuration; the Azure platform provides the underlying network fabric.

## Azure resources and their roles

| Component | Current configuration | What it does |
|---|---|---|
| Region | Central India | Region hosting the VNet, NIC, public IP, and VM. |
| VNet | `vnet-netlab-ci-01`, `10.20.0.0/16` | The VM's private IPv4 network. The address range is private and is not directly routable over the public internet. There are no VNet peerings. |
| Subnet | `snet-app-01`, `10.20.1.0/24` | A smaller address range inside the VNet containing the VM NIC. It has no subnet-level NSG or custom route table associated. `defaultOutboundAccess` is disabled. |
| VM | `vm-netlab-ci-01` | Ubuntu 24.04 VM running the API container. It has one NIC and no data disks. |
| NIC | `nic-netlab-ci-01` | The VM's virtual network interface. Its primary IP configuration joins `snet-app-01`, has private IP `10.20.1.4` (dynamic allocation), and is associated with the public IP and NSG below. Accelerated networking and IP forwarding are disabled. |
| Public IP | `pip-netlab-ci-01` | Standard SKU, static IPv4 `20.204.42.165`, associated with the NIC's primary IP configuration. Azure DNS name: `netlab-rahul-20261018.centralindia.cloudapp.azure.com`. |
| NSG | `nsg-netlab-01` | Attached to the NIC. Filters network traffic; it is not a router, proxy, or firewall appliance running inside the VM. |
| Route table | None associated | No custom routes or forced-tunnel route table is configured; Azure uses its system routes. |
| NAT Gateway | None | No NAT Gateway resource is configured. |
| Load balancer | None | The public IP is associated directly with this VM's NIC; there is no load balancer in this request path. |
| DDoS protection | Not enabled on the VNet | The VNet reports DDoS protection disabled. |

The VM has a **private** IP on its NIC. The public IP is a separate Azure resource associated with that NIC IP configuration; the public IP is not an address from the VNet's `10.20.0.0/16` range.

## Network security group rules

The NSG is associated with the NIC, so traffic to and from that NIC is evaluated against its rules. Explicit rules are evaluated by ascending priority number; a matching rule with a lower number is evaluated first.

| Priority | Direction | Protocol / destination port | Source | Action | Purpose |
|---:|---|---|---|---|---|
| 100 | Inbound | TCP 22 | `49.37.129.153/32` | Allow | SSH from the configured client IPv4 address only. Update this rule if that address changes. |
| 105 | Inbound | TCP 443 | Any (`*`) | Allow | Public HTTPS access to the API. |
| 65000 | Inbound | Any | `VirtualNetwork` | Allow | Azure default: traffic from the virtual network. |
| 65001 | Inbound | Any | `AzureLoadBalancer` | Allow | Azure default platform rule. No load balancer is part of this API path. |
| 65500 | Inbound | Any | Any | Deny | Azure default: deny remaining inbound traffic. |
| 65000 | Outbound | Any | Any, destination `VirtualNetwork` | Allow | Azure default: outbound within the virtual network. |
| 65001 | Outbound | Any | Any, destination `Internet` | Allow | Azure default outbound security rule. |
| 65500 | Outbound | Any | Any | Deny | Azure default: deny remaining outbound traffic. |

The NSG has **no inbound rule for TCP 5080**. The old public HTTP endpoint is closed. The SSH rule's source is a single IPv4 address (`/32`), while HTTPS is intentionally reachable from any source address for this study.

NSGs are stateful: once an allowed connection is established, its response traffic is allowed back without a separate inbound rule for the ephemeral client port. An NSG allow rule only permits traffic through that filter; it does not itself create a route or make a service listen. For this API, the public IP association, Azure routing, host/container port publishing, and Kestrel listener must also be in place.

## How a request from the client reaches the API

For the `InternetDemo` profile, `HelloClient` uses:

```text
https://netlab-rahul-20261018.centralindia.cloudapp.azure.com
```

The sequence is:

1. **Name lookup:** The client asks DNS to resolve the Azure DNS name. Azure's DNS label currently resolves to public IP `20.204.42.165`. DNS supplies the destination address; it does not carry the API request.
2. **Connection from the PC:** The client opens a TCP connection from an ephemeral local source port to the resolved public IPv4 address on destination port `443`. The traffic leaves the PC's network, passes through its router (if present) and ISP, then crosses the public internet toward Azure.
3. **Azure public-IP association:** Azure receives traffic for `20.204.42.165` and maps it to the primary IP configuration of `nic-netlab-ci-01`. This public address is not the VM's private `10.20.1.4` address.
4. **NSG check:** The NIC's NSG evaluates the inbound connection. TCP 443 from any source matches the allow rule at priority 105. Other inbound traffic is denied unless another rule permits it; SSH is restricted to the configured `/32`.
5. **Delivery to the VM:** Azure delivers the permitted traffic through the NIC to the VM. The destination service is HTTPS on port 443.
6. **Container port publishing:** Docker publishes host port 443 to port 443 in the API container (`443:443`). No API traffic goes through a reverse proxy or load balancer.
7. **TLS and HTTP:** Kestrel in the container presents the study certificate and terminates TLS. The HTTPS request, including the bearer token when calling `/api/me`, is encrypted between the client and Kestrel. Kestrel then passes the HTTP request to the ASP.NET Core API.
8. **Response:** The API response travels back over the established connection. Azure's network state and the VM's networking deliver it to the client; HTTPS encrypts the response on the connection as well.

The certificate is self-signed, not issued by a public certificate authority. The client must trust it and the requested DNS name must match a certificate name. This study certificate has both the public DNS name and `localhost` in its Subject Alternative Name. Trust is currently installed only for the current Windows user on this PC.

## Sign-in is a separate network conversation

When `HelloClient` signs in, MSAL launches the system browser and communicates with Microsoft Entra over HTTPS. After sign-in, the client receives an access token. It then makes the API request described above and sends that token as a bearer token over HTTPS. The API validates the token and its `access_as_user` scope.

Thus there are two different destinations:

```text
Browser / MSAL  -- HTTPS --> Microsoft Entra (sign-in and token)
HelloClient     -- HTTPS --> Azure VM (API request with token)
```

The API token is not sent to the VM as part of the sign-in redirect; the client attaches it to its API request.

## Local Development profile and SSH tunnel

The `Development` profile instead calls `https://localhost:5443`. In that mode, the client connects to a local port on this PC. An SSH tunnel forwards that connection to the VM's loopback address on port 443:

```powershell
ssh.exe -i "$HOME\.ssh\id_ed25519_netlab_ci" -N -L 5443:127.0.0.1:443 azureuser@20.204.42.165
```

The SSH connection travels from the PC to the VM's **public IP on TCP 22**, allowed only from the configured source `/32`. The SSH client listens on local port 5443 and forwards the byte stream through SSH to `127.0.0.1:443` on the VM. HTTPS/TLS still terminates at Kestrel; the tunnel does not replace TLS. The `localhost` certificate name matches the URL used by the client.

```text
HelloClient -> localhost:5443 -> encrypted SSH tunnel -> VM 127.0.0.1:443
            -> Docker-published port -> container Kestrel
```

Without the SSH tunnel running, the Development profile cannot reach the API. The `InternetDemo` profile does not use localhost or this tunnel.

## What is not in this design

- No reverse proxy (such as Nginx), Application Gateway, Azure Load Balancer, or API Management.
- No VPN or private link between the PC and the VNet; the InternetDemo request uses the public endpoint.
- No NAT Gateway or custom route table.
- No public TCP 5080 rule; HTTPS uses TCP 443.
- No inbound rule for arbitrary ports. Inbound access is limited to the two explicit rules and Azure's default rules.

The public endpoint and self-signed certificate are for learning, not a production design. Keep the VM deallocated when you are finished to stop VM compute charges.
