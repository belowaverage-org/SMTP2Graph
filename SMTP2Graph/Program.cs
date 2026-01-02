using Microsoft.Graph;
using Azure.Identity;
using System.Net.Sockets;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.Kiota.Abstractions;

Console.WriteLine("Initializing...");
var mail_listener = new TcpListener(IPAddress.Any, 25);
var scopes = new[] { "https://graph.microsoft.com/.default" };

var options = new ClientSecretCredentialOptions
{
    AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
};

var clientSecretCredential = new ClientSecretCredential(
    Environment.GetEnvironmentVariable("TENANT_ID"),
    Environment.GetEnvironmentVariable("CLIENT_ID"),
    Environment.GetEnvironmentVariable("CLIENT_SECRET"),
    options
);

Console.WriteLine("Setting up MS Graph Service Client...");
var graphClient = new GraphServiceClient(clientSecretCredential, scopes);

Console.WriteLine("Starting SMTP listener loop...");
mail_listener.Start();
while (true)
{
    var client = mail_listener.AcceptTcpClient();
    Console.WriteLine("Accepting connection, attempting to process...");
    _ = Task.Run(() => {
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var sw = new StreamWriter(stream);
            var sr = new StreamReader(stream);
            sw.AutoFlush = true;
            sw.Write($"220 Who are you...?\r\n");
            HandleConnection(client, stream, sw, sr);
        }
        catch (Exception e)
        {
            if (client.Connected) client.Close();
            Console.WriteLine("An error occured in the SMTP listener loop:");
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
        }
    });
}

void HandleConnection(TcpClient client, Stream stream, StreamWriter sw, StreamReader sr)
{
    string From = string.Empty;
    string MIME = string.Empty;
    while (true)
    {
        var msg = sr.ReadLine();
        if (msg.StartsWith("HELO") || msg.StartsWith("EHLO"))
        {
            Console.WriteLine("HELO");
            sw.Write("250 Whatchu want?\r\n");
            continue;
        }
        if (msg.StartsWith("MAIL FROM:"))
        {
            Console.WriteLine("MAIL FROM");
            From = Regex.Match(msg, "<(.*)>").Groups[1].Value;
            sw.Write("250 You again?!\r\n");
            continue;
        }
        if (msg.StartsWith("RCPT TO:"))
        {
            Console.WriteLine("RCPT TO");
            sw.Write("250 Ok fine...\r\n");
            continue;
        }
        if (msg.StartsWith("DATA"))
        {
            Console.WriteLine("DATA");
            sw.Write("354 And whats so important that you have to bother me...?\r\n");
            MIME = ReadMIME(sr);
            sw.Write("250 Oh yea, that was *very* important... SMH.\r\n");
            Console.WriteLine("Sending to MS Graph...");
            SendMessage(From, MIME);
            Console.WriteLine("Sent.");
            continue;
        }
        if (msg.StartsWith("QUIT"))
        {
            Console.WriteLine("QUIT");
            sw.Write("221 Bye, don't talk to me...\r\n");
            break;
        }
    }
    Console.WriteLine("Closing connection...");
    client.Close();
    Console.WriteLine("Done.");
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
    var request = new RequestInformation();
    request.URI = new($"https://graph.microsoft.com/v1.0/users/{From}/sendMail");
    request.HttpMethod = Method.POST;
    request.Headers.Add("Content-Type", "text/plain");
    request.Content = new MemoryStream(mimeb64bytes);
    _ = graphClient.RequestAdapter.SendNoContentAsync(request);
}