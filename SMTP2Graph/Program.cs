using Microsoft.Graph;
using Azure.Identity;
using Microsoft.Graph.Models;

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


var graphClient = new GraphServiceClient(clientSecretCredential, scopes);

await graphClient.Users["support@belowaverage.org"].SendMail.PostAsync(new()
{
    Message = new()
    {
        From = new() { EmailAddress = new() { Address = "support@belowaverage.org" } },
        ToRecipients = [new() { EmailAddress = new() { Address = "dylan@belowaverage.org" } }],
        Subject = "Hi",
        Body = new() { ContentType = BodyType.Html, Content = "Hi" }
    },
    SaveToSentItems = false
});