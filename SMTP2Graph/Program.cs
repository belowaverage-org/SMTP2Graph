using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;

namespace SMTP2Graph
{
    public static class Program
    {
        public static void Main()
        {
            HostApplicationBuilder appBuilder = Host.CreateApplicationBuilder();
            appBuilder.Services.AddWindowsService((opt) => {
                opt.ServiceName = "SMTP2Graph";
            });
            appBuilder.Logging.SetMinimumLevel(LogLevel.Trace);
            appBuilder.Logging.AddSimpleConsole((sc) => {
                sc.SingleLine = true;
                sc.TimestampFormat = "MM/dd/yyyy: hh:mm:ss: ";
            });
            if (OperatingSystem.IsWindows())
            {
                appBuilder.Logging.AddFilter<EventLogLoggerProvider>((level) => true);
                appBuilder.Logging.AddEventLog();
            }
            appBuilder.Services.AddHostedService<Worker>();
            IHost host = appBuilder.Build();
            host.Run();
        }
    }
}