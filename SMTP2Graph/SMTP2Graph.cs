using Microsoft.Graph;
using Azure.Identity;
using System.Net.Sockets;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.Kiota.Abstractions;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;

namespace SMTP2Graph;

class Service
{
    private static ILogger<Service> Logger = LoggerFactory.Create((lf) => {
        lf.AddSimpleConsole((sc) => {
            sc.SingleLine = true;
            sc.TimestampFormat = "MM/dd/yyyy: hh:mm:ss: ";
        });
        if (OperatingSystem.IsWindows()) lf.AddEventLog();
    }).CreateLogger<Service>();

    public static void Main()
    {
        Logger.LogInformation("Initializing SMTP2Graph...");
        var mail_listener = new TcpListener(IPAddress.Any, 25);
        var scopes = new[] { "https://graph.microsoft.com/.default" };

        var options = new ClientSecretCredentialOptions
        {
            AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
        };

        Logger.LogInformation("Connecting to MS Graph...");

        GraphServiceClient graphClient = null;

        try
        {
            var clientSecretCredential = new ClientSecretCredential(
                GetConfigOption("TENANT_ID"),
                GetConfigOption("CLIENT_ID"),
                GetConfigOption("CLIENT_SECRET"),
                options
            );
            graphClient = new GraphServiceClient(clientSecretCredential, scopes);
        }
        catch (Exception e)
        {
            Logger.LogCritical(e, "Failed to connect to MS Graph.");
            Environment.Exit(0);
        }

        Logger.LogInformation("Starting SMTP listener loop, ready for connections.");
        mail_listener.Start();
        while (true)
        {
            var client = mail_listener.AcceptTcpClient();
            Logger.LogInformation("Accepting connection, attempting to process...");
            _ = Task.Run(() => {
                try
                {
                    client.NoDelay = true;
                    var stream = client.GetStream();
                    var sw = new StreamWriter(stream);
                    var sr = new StreamReader(stream);
                    sw.AutoFlush = true;
                    sw.Write($"220 {Environment.MachineName} SMTP2Graph Service ready\r\n");
                    HandleConnection(client, stream, sw, sr);
                }
                catch (Exception e)
                {
                    if (client.Connected) client.Close();
                    Logger.LogError(e, "An error occured in the SMTP listener loop.");
                }
            });
        }

        string GetConfigOption(string Key)
        {
            string? env;
            env = Environment.GetEnvironmentVariable(Key);
            if (env != null) return env;
            if (OperatingSystem.IsWindows()) env = (string?)(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SMTP2Graph")?.GetValue(Key));
            if (env != null) return env;
            Logger.LogCritical($"Config option not set: {Key}.");
            Environment.Exit(0);
            return string.Empty;
        }

        void HandleConnection(TcpClient client, Stream stream, StreamWriter sw, StreamReader sr)
        {
            string From = string.Empty;
            string MIME = string.Empty;
            while (true)
            {
                var msg = sr.ReadLine();
                if (msg == null) continue;
                if (msg.StartsWith("HELO") || msg.StartsWith("EHLO"))
                {
                    Logger.LogInformation("HELO");
                    sw.Write("250 OK\r\n");
                    continue;
                }
                if (msg.StartsWith("MAIL FROM:"))
                {
                    Logger.LogInformation("MAIL FROM");
                    #pragma warning disable SYSLIB1045
                    From = Regex.Match(msg, "<(.*)>").Groups[1].Value;
                    #pragma warning restore SYSLIB1045
                    sw.Write("250 OK\r\n");
                    continue;
                }
                if (msg.StartsWith("RCPT TO:"))
                {
                    Logger.LogInformation("RCPT TO");
                    sw.Write("250 OK\r\n");
                    continue;
                }
                if (msg.StartsWith("DATA"))
                {
                    Logger.LogInformation("DATA");
                    sw.Write("354 Start mail input; end with <CRLF>.<CRLF>\r\n");
                    MIME = ReadMIME(sr);
                    sw.Write("250 OK\r\n");
                    Logger.LogInformation("Sending to MS Graph...");
                    SendMessage(From, MIME);
                    Logger.LogInformation("Sent.");
                    continue;
                }
                if (msg.StartsWith("QUIT"))
                {
                    Logger.LogInformation("QUIT");
                    sw.Write($"221 {Environment.MachineName} Service closing transmission channel\r\n");
                    break;
                }
            }
            Logger.LogInformation("Closing connection...");
            client.Close();
            Logger.LogInformation("Done.");
        }

        string ReadMIME(StreamReader sr)
        {
            string MIME = string.Empty;
            while (true)
            {
                var msg = sr.ReadLine();
                if (msg == ".") break;
                if (msg == "..")
                {
                    msg = ".";
                }
                MIME += msg + "\r\n";
            }
            return MIME;
        }

        void SendMessage(string From, string MIME)
        {
            var mimeb64bytes = Encoding.UTF8.GetBytes(
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(MIME)
                )
            );
            var request = new RequestInformation
            {
                URI = new($"https://graph.microsoft.com/v1.0/users/{From}/sendMail"),
                HttpMethod = Method.POST
            };
            request.Headers.Add("Content-Type", "text/plain");
            request.Content = new MemoryStream(mimeb64bytes);
            _ = graphClient.RequestAdapter.SendNoContentAsync(request);
        }
    }
}