# Azure networking and Entra learning playground

## Current setup

All deployed resources are in **Central India**. The existing resource group `rahulkum-rg` has its metadata location set to East US 2; Azure resource groups can contain resources in other regions.

| Resource | Name | Learning purpose |
|---|---|---|
| Resource group | `rahulkum-rg` | Holds this playground's Azure resources |
| Virtual network | `vnet-netlab-ci-01` (`10.20.0.0/16`) | Private network address space |
| Subnet | `snet-app-01` (`10.20.1.0/24`) | VM network segment |
| Network security group | `nsg-netlab-01` | SSH restricted to the supplied client `/32`; HTTPS TCP 443 open |
| Linux VM | `vm-netlab-ci-01` | Ubuntu 24.04, `Standard_B2s`, one 32-GiB Standard SSD LRS OS disk |
| NIC | `nic-netlab-ci-01` | Connects the VM to the subnet |
| Public IP | `pip-netlab-ci-01` | SSH and direct HTTPS API access |
| Entra API registration | `netlab-api` | API audience and delegated `access_as_user` scope |
| Entra client registration | `netlab-client` | Public/native client; signs in the user in the system browser |

The VM's private IP is currently `10.20.1.4`. The static public IP is currently `20.204.42.165`, with DNS name `netlab-rahul-20261018.centralindia.cloudapp.azure.com`. No load balancer, Redis, AKS cluster, monitoring add-on, or separate data disk is deployed. The VM is currently **running** for the HTTPS exercise.

See [NETWORKING-BASICS.md](NETWORKING-BASICS.md) for beginner explanations of the concepts, and [NETWORKING.md](NETWORKING.md) for the detailed live configuration and request path.
The Azure resource relationship diagram is in [AZURE-RESOURCE-ASSOCIATIONS.md](AZURE-RESOURCE-ASSOCIATIONS.md).

## C# application

The source is under this folder:

- `HelloApi`: ASP.NET Core Minimal API protected by Microsoft Identity Web.
- `HelloClient`: C# public client using MSAL to sign in a user and call the API. The client runs on your PC; it is not deployed to Azure.
- `HelloApi\Dockerfile`: runtime image definition. Kestrel terminates TLS directly; no reverse proxy is used.

The API has two routes:

- `GET /hello` is public.
- `GET /api/me` requires a valid Entra access token with the delegated `access_as_user` scope.

The API app registration ID (also its v2 token audience) is `2585a3fe-01db-4983-8737-698a2fe45f36`. The client app registration ID is `eae18a78-0374-4725-80fb-00f2f7af0dd2`. The tenant ID is `f95c26ee-b66b-458e-b8cb-ee5d87e8092a`. These IDs identify registrations; they are not credentials. No client secret is used.

## Request and network flow

```text
C# client on this PC
  ├─ signs the user in with Microsoft Entra (system browser)
  └─ sends GET /api/me with a bearer token
       └─ HTTPS to Azure DNS name → VM public IP:443
            └─ NSG allows TCP 443 → Kestrel in the API container
                 └─ ASP.NET Core validates token and scope
```

Kestrel terminates TLS directly on port `443`; there is no reverse proxy. The API uses a self-signed certificate with the public DNS name and `localhost` in its Subject Alternative Name. Its public part is trusted in the current Windows user's **Trusted Root Certification Authorities** store. The password-protected PFX and its password are stored on the VM outside the container image, with restrictive permissions. The PFX is mounted read-only in the container and is not in source control.

HTTPS encrypts the network connection, but a self-signed certificate is not a public identity: only devices that explicitly trust this certificate will accept it. The private key must remain private. The certificate expires on October 18, 2028 and must be replaced manually if used beyond then. For this v2 token, `aud` is the API app's client ID and the required delegated scope is `api://<api-client-id>/access_as_user`.

The `InternetDemo` client profile uses the public DNS URL. The `Development` profile uses `https://localhost:5443` via the SSH tunnel below; the certificate includes `localhost`, so the client can validate its name. AKS `kubectl port-forward` is another local development path, not an Azure Load Balancer.

## Start the playground

Open PowerShell. If `az` isn't available on `PATH`, use the installed Azure CLI path below:

```powershell
$az = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'
& $az account set --subscription d864c4d6-0ac8-4121-a607-e6f67140db31
& $az vm start --resource-group rahulkum-rg --name vm-netlab-ci-01
```

If Azure asks for MFA, sign in with `& $az login --use-device-code --tenant f95c26ee-b66b-458e-b8cb-ee5d87e8092a`.

If your public IP changes, update the existing SSH rule to your current IPv4 `/32` before connecting:

```powershell
& $az network nsg rule update --resource-group rahulkum-rg --nsg-name nsg-netlab-01 --name Allow-SSH-From-Learning-IP --source-address-prefixes <your-current-ip>/32
```

To use the `Development` profile through an SSH tunnel, start this in one PowerShell window and leave it open:

```powershell
ssh.exe -i "$HOME\.ssh\id_ed25519_netlab_ci" -N -L 5443:127.0.0.1:443 azureuser@20.204.42.165
```

Entra settings are read from `HelloClient\appsettings.json`; `Development` points at `https://localhost:5443` in `HelloClient\appsettings.Development.json`. No per-run environment variables are needed.

```powershell
Invoke-RestMethod https://localhost:5443/hello

dotnet run --project .\HelloClient\HelloClient.csproj
```

For the direct internet demo, no SSH tunnel is required. The `InternetDemo` profile uses the Azure DNS URL configured in `HelloClient\appsettings.json`:

```powershell
Invoke-RestMethod https://netlab-rahul-20261018.centralindia.cloudapp.azure.com/hello
dotnet run --project .\HelloClient\HelloClient.csproj --launch-profile InternetDemo
```

The client opens the system browser for Entra sign-in, then prints the authenticated user's name and object ID. Without a token, `/api/me` returns HTTP 401; after sign-in, it returns HTTP 200. The NSG permits TCP 443 but no longer permits public TCP 5080.

The certificate artifacts are stored outside the repository at `%USERPROFILE%\.ssh\netlab-api-study.pfx`, `%USERPROFILE%\.ssh\netlab-api-study.cer`, and `%USERPROFILE%\.ssh\netlab-api-study-password.dpapi`. The password file is protected with Windows DPAPI for the current user. To remove the trust after the study, delete the certificate with thumbprint `66FE185CED06E479BBD8CAE7B191EA27209D5F70` from **Current User → Trusted Root Certification Authorities → Certificates**, or run:

```powershell
certutil.exe -user -delstore Root 66FE185CED06E479BBD8CAE7B191EA27209D5F70
```

After removing trust, the client will reject this study certificate until you trust a replacement.

The container is configured to restart with Docker after a VM restart. If it isn't running after starting the VM, connect over SSH and run `sudo docker start hello-api`.

## End a learning session and costs

Close the SSH tunnel with Ctrl+C, then deallocate the VM:

```powershell
& $az vm deallocate --resource-group rahulkum-rg --name vm-netlab-ci-01
```

Deallocation stops VM compute billing. The OS disk and static public IP remain allocated and can continue to cost money. Central India retail list-price estimates checked on October 18, 2026 are approximately:

| Resource | Approximate cost |
|---|---:|
| `Standard_B2s` Linux VM | $0.0448 per running hour |
| 32-GiB Standard SSD LRS OS disk | $2.64 per month |
| Standard static public IP | $0.006 per hour (about $4.38 per 730-hour month) |

For example, about four VM hours per week is roughly $0.78/month in compute, plus around $2.64/month for the disk and $4.38/month for the retained public IP. These are retail estimates in USD before taxes, transfer, and any subscription credit or discounts; check Cost Management for actual charges. Deleting the public IP after an exercise avoids its ongoing charge, but you'll need to create/re-attach an IP and update the SSH destination before connecting again.

No daily schedule is configured: deallocate the VM at the end of each session, then start it manually when you resume. Disk, VNet, and NSG state remain so the exercises can continue.
