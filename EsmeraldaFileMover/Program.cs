using System.Diagnostics;
using System.ServiceProcess;

namespace EsmeraldaFileMover
{
    class Program
    {
        static void Main()
        {
            if (!EventLog.SourceExists("EsmeraldaFileMover"))
            {
                EventLog.CreateEventSource("EsmeraldaFileMover", "EsmeraldaFileMover");
            }
            var services = new ServiceBase[] { new Service() };
            services[0].EventLog.Source = "EsmeraldaFileMover";
            services[0].EventLog.Log = "EsmeraldaFileMover";
#if DEBUG
            var fileMover = new FileMover(services[0].EventLog);
            fileMover.Start();
            System.Console.WriteLine("Service started...");
            System.Console.WriteLine("Press ENTER to stop service");
            System.Console.ReadLine();
            fileMover.Stop();
#else
            ServiceBase.Run(services);
#endif
        }
    }
}
