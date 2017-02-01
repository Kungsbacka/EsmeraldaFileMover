using System.ServiceProcess;

namespace EsmeraldaFileMover
{
    public class Service : ServiceBase
    {
        private FileMover fileMover;

        public Service()
        {
        }

        protected override void OnStart(string[] args)
        {
            fileMover = new FileMover(EventLog);
            fileMover.Start();
        }

        protected override void OnStop()
        {
            if (null != fileMover)
            {
                fileMover.Stop();
            }
        }
    }
}
