# SMTP2Graph

SMTP2Graph is a lightweight SMTP listener that accepts incoming email messages over TCP (port 25) and forwards them to Microsoft 365 mailboxes using the Microsoft Graph API. This allows you to relay SMTP traffic into Exchange Online without exposing traditional SMTP relay services.

## Features
- Minimal SMTP command support (HELO/EHLO, MAIL FROM, RCPT TO, DATA, QUIT)
- Converts incoming MIME messages to Base64 and sends via Microsoft Graph `/users/{id}/sendMail`
- Runs as a standalone .NET application or inside Docker

## Requirements
- .NET 9.0 SDK for building
- Microsoft Graph API credentials (App registration with `Mail.Send` permission)

## Environment Variables
| Variable       | Description                                      |
|---------------|--------------------------------------------------|
| `TENANT_ID`   | Azure AD tenant ID                               |
| `CLIENT_ID`   | App registration client ID                       |
| `CLIENT_SECRET`| App registration client secret                  |

## Usage
### Build and Run Locally
```bash
dotnet restore
dotnet publish SMTP2Graph -c Release -o ./publish
cd publish
TENANT_ID="<tenant>" CLIENT_ID="<client>" CLIENT_SECRET="<secret>" ./SMTP2Graph
```

### Docker Pull and Run
```bash
# Pull the latest image
docker pull ghcr.io/belowaverage-org/smtp2graph:latest

# Run the container
docker run -d \
  -e TENANT_ID="<tenant>" \
  -e CLIENT_ID="<client>" \
  -e CLIENT_SECRET="<secret>" \
  -p 25:25 ghcr.io/belowaverage-org/smtp2graph:latest
```

## Security Considerations
- **Do not expose this container publicly without authentication and TLS.**
- Implement IP allowlists or run behind a secure proxy.
- Use Application Access Policies in Microsoft 365 to restrict which mailboxes the app can send as.

## How It Works
1. Listens on port 25 for SMTP commands.
2. Reads MIME content after `DATA` command.
3. Encodes MIME as Base64 and sends via Microsoft Graph using `sendMail` endpoint.
