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

public static class Service
{
    private readonly static ILoggerFactory LogFactory = LoggerFactory.Create((lf) => {
        lf.SetMinimumLevel(LogLevel.Trace);
        lf.AddSimpleConsole((sc) => {
            sc.SingleLine = true;
            sc.TimestampFormat = "MM/dd/yyyy: hh:mm:ss: ";
        });
        if (OperatingSystem.IsWindows()) lf.AddEventLog();
    });

    private static readonly string[] GraphScopes = ["https://graph.microsoft.com/.default"];

    private static readonly ClientSecretCredentialOptions GraphCredOptions = new() {
        AuthorityHost = AzureAuthorityHosts.AzurePublicCloud
    };

    private static GraphServiceClient? GraphClient;

    private static readonly ILogger Logger = LogFactory.CreateLogger(typeof(Service));

    private static readonly TcpListener MailListener = new(IPAddress.Any, 25);

    public static void Main()
    {
        Logger.LogInformation("Initializing SMTP2Graph...");

        Logger.LogInformation("Connecting to MS Graph...");

        try
        {
            var clientSecretCredential = new ClientSecretCredential(
                GetConfigOption("TENANT_ID"),
                GetConfigOption("CLIENT_ID"),
                GetConfigOption("CLIENT_SECRET"),
                GraphCredOptions
            );
            GraphClient = new GraphServiceClient(clientSecretCredential, GraphScopes);
        }
        catch (Exception e)
        {
            Logger.LogCritical(e, "Failed to connect to MS Graph.");
            Environment.Exit(0);
        }

        Logger.LogInformation("Starting SMTP listener loop, ready for connections.");

        try
        {
            MailListener.Start();
        }
        catch (Exception e)
        {
            Logger.LogCritical(e, "Could not bind to port.");
            Environment.Exit(0);
        }

        while (true)
        {
            var client = MailListener.AcceptTcpClient();
            var port = ((IPEndPoint)client.Client.RemoteEndPoint!).Port;
            Logger.LogInformation(port, "Accepting connection, attempting to process...");
            _ = Task.Run(async () => {
                try
                {
                    client.NoDelay = true;
                    var stream = client.GetStream();
                    await HandleConnection(client, stream, port);
                }
                catch (Exception e)
                {
                    if (client.Connected) client.Close();
                    Logger.LogError(port, e, "An error occured in the SMTP listener loop.");
                }
            });
        }
    }

    private static string GetConfigOption(string Key)
    {
        string? env;
        env = Environment.GetEnvironmentVariable(Key);
        if (env != null) return env;
        if (OperatingSystem.IsWindows()) env = (string?)(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SMTP2Graph")?.GetValue(Key));
        if (env != null) return env;
        Logger.LogCritical("Config option not set: {Key}.", Key);
        Environment.Exit(0);
        return string.Empty;
    }

    private static async Task HandleConnection(TcpClient client, Stream stream, int Port)
    {
        string From = string.Empty;
        var sw = new StreamWriter(stream);
        var sr = new StreamReader(stream);
        sw.AutoFlush = true;
        sw.Write($"220 {Environment.MachineName} SMTP2Graph Service ready\r\n");
        while (true)
        {
            string? msg = null;
            try
            {
                msg = await sr.ReadLineAsync();
            }
            catch
            {
                Logger.LogInformation(Port, "Client disconnected.");
                break;
            }
            if (msg == null) continue;
            Logger.LogTrace(Port, "Recieved: {msg}", msg);
            if (msg.StartsWith("HELO") || msg.StartsWith("EHLO"))
            {
                await sw.WriteAsyncExt("250 OK", Port);
                continue;
            }
            if (msg.StartsWith("MAIL FROM:"))
            {
                #pragma warning disable SYSLIB1045
                From = Regex.Match(msg, "<(.*)>").Groups[1].Value;
                #pragma warning restore SYSLIB1045
                await sw.WriteAsyncExt("250 OK", Port);
                continue;
            }
            if (msg.StartsWith("RCPT TO:"))
            {
                await sw.WriteAsyncExt("250 OK", Port);
                continue;
            }
            if (msg.StartsWith("DATA"))
            {
                await sw.WriteAsyncExt("354 Start mail input; end with <CRLF>.<CRLF>", Port);
                var MIME = await ReadMIME(sr);
                Logger.LogTrace(Port, "Recieved: {Message}", MIME[..(MIME.Length > 500 ? 500 : MIME.Length)]);
                await sw.WriteAsyncExt("250 OK", Port);
                await SendMessage(From, MIME, Port);
                continue;
            }
            if (msg.StartsWith("QUIT"))
            {
                await sw.WriteAsyncExt($"221 {Environment.MachineName} Service closing transmission channel", Port);
                break;
            }
        }
        Logger.LogInformation(Port, "Closing connection...");
        client.Close();
    }

    private static async Task<string> ReadMIME(StreamReader SR)
    {
        var mime = new List<string?>();
        while (true)
        {
            var msg = await SR.ReadLineAsync();
            if (msg == ".") break;
            if (msg == "..")
            {
                msg = ".";
            }
            mime.Add(msg);
        }
        return string.Join("\r\n", mime);
    }

    private static async Task SendMessage(string From, string MIME, int Port)
    {
        Logger.LogInformation(Port, "Sending MIME to MS Graph...");
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
        if (GraphClient != null) await GraphClient.RequestAdapter.SendNoContentAsync(request);
        Logger.LogInformation(Port, "Message sent to MS Graph.");
    }

    private static async Task WriteAsyncExt(this StreamWriter SW, string? Message, int Port)
    {
        Logger.LogTrace(Port, "Sent: {Message}", Message);
        await SW.WriteAsync($"{Message}\r\n");
    }
}