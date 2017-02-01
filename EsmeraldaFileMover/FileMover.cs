using System;
using System.Diagnostics;
using System.IO;
using System.Configuration;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Xml.Linq;
using System.Security.AccessControl;

namespace EsmeraldaFileMover
{
    class FileMover
    {
        private EventLog eventLog;
        private volatile bool stop = false;
        private bool shouldResetPermissions = false;
        private string inboxDirectory;
        private string xmlDestinationDirectory;
        private string attachmentDestinationDirectory;
        private HashSet<int> loggedTargets;
        private Dictionary<string, FailInfo> failedBundles;

        class FailInfo
        {
            public DateTime FirstFailTime { get; } = DateTime.Now;
            public bool Retry { get; set; }

            public FailInfo(bool retry)
            {
                Retry = retry;
            }
        }

        public FileMover(EventLog eventLog)
        {
            this.eventLog = eventLog;
            loggedTargets = new HashSet<int>();
            failedBundles = new Dictionary<string, FailInfo>();
        }

        public void Start()
        {
            inboxDirectory = ConfigurationManager.AppSettings["SourceDirectory"];
            if (!Directory.Exists(inboxDirectory))
            {
                Log("Invalid source directory", inboxDirectory, null, 0);
                return;
            }
            xmlDestinationDirectory = ConfigurationManager.AppSettings["XmlDestinationDirectory"];
            if (!Directory.Exists(xmlDestinationDirectory))
            {
                Log("Invalid xml destination directory", xmlDestinationDirectory, null, 0);
                return;
            }
            attachmentDestinationDirectory = ConfigurationManager.AppSettings["AttachmentDestinationDirectory"];
            if (!Directory.Exists(attachmentDestinationDirectory))
            {
                Log("Invalid attachment destination directory", attachmentDestinationDirectory, null, 0);
                return;
            }
            string value = ConfigurationManager.AppSettings["ResetPermissionsAfterMove"];
            if (!bool.TryParse(value, out shouldResetPermissions))
            {
                Log("Invalid value for ResetPermissionsAfterMover. Valid values are \"true\" or \"false\"", value, null, 0);
            }
            Thread thread = new Thread(delegate ()
            {
                try
                {
                    MoveFiles();
                    eventLog.WriteEntry("Service stopped.", EventLogEntryType.Information, 0);
                }
                catch (Exception ex)
                {
                    Log("An unhandeled exception occured. Service stopped.", null, ex, 0);
                }
            });
            thread.Start();
            eventLog.WriteEntry("Service started.", EventLogEntryType.Information, 0);
        }

        public void Stop()
        {
            stop = true;
        }

        private void Log(string msg, string target, Exception ex, int id)
        {
            if (null == eventLog)
            {
                return;
            }
            // Check if we have logged this message before to avoid spamming the event log.
            int hash = $"{id}:{target}".GetHashCode();
            if (loggedTargets.Contains(hash))
            {
                return;
            }
            loggedTargets.Add(hash);
            eventLog.WriteEntry(
                $"{msg}\r\n\r\nTarget: {target}\r\n\r\n{ex?.ToString()}",
                id < 20 ? EventLogEntryType.Error : EventLogEntryType.Warning,
                id
            );
        }

        private string GetDestinationDirectory(string fileName)
        {
            if (fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                return xmlDestinationDirectory;
            }
            return attachmentDestinationDirectory;
        }

        private void MoveFiles()
        {
            while (!stop)
            {
                string[] xmlFiles = Directory.GetFiles(inboxDirectory, "*.xml", SearchOption.AllDirectories);
                foreach (string xmlFile in xmlFiles)
                {
                    if (stop)
                    {
                        return;
                    }
                    string xmlFileName = Path.GetFileName(xmlFile);
                    string sourceDir = Path.GetDirectoryName(xmlFile);
                    FailInfo info;
                    if (failedBundles.TryGetValue(xmlFileName, out info))
                    {
                        if (!info.Retry)
                        {
                            continue;
                        }
                        if (DateTime.Now - info.FirstFailTime > TimeSpan.FromHours(24))
                        {
                            Log("Time limit exceeded for files in target folder. No more attempts will be made to move the files.", sourceDir, null, 10);
                            info.Retry = false;
                            continue;
                        }
                    }
                    var filesToMove = new List<string>();
                    XDocument doc = null;
                    filesToMove.Add(xmlFileName);
                    try
                    {
                        doc = XDocument.Load(xmlFile);
                    }
                    catch (IOException ex)
                    {
                        Log("Unable to read xml document", xmlFile, ex, 10);
                        if (!failedBundles.TryGetValue(xmlFileName, out info))
                        {
                            failedBundles.Add(xmlFileName, new FailInfo(true));
                        }
                        continue;
                    }
                    IEnumerable<string> attachments =
                        from attachment in doc.Descendants("bifogadfil")
                        select (string)(attachment.Element("filnamn"));
                    foreach (string item in attachments)
                    {
                        if (!filesToMove.Exists(t => t.Equals(item, StringComparison.OrdinalIgnoreCase)))
                        {
                            filesToMove.Add(item);
                        }
                    }
                    bool canMove = true;
                    foreach (string file in filesToMove)
                    {
                        string sourcePath = Path.Combine(sourceDir, file);
                        string destPath = Path.Combine(GetDestinationDirectory(file), file);
                        if (!File.Exists(sourcePath))
                        {
                            Log("File does not exist", sourcePath, null, 10);
                            canMove = false;
                            break;
                        }
                        if (File.Exists(destPath))
                        {
                            Log("File already exists", destPath, null, 10);
                            canMove = false;
                            break;
                        }
                        try
                        {
                            using (File.Open(sourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                        }
                        catch (IOException)
                        {
                            Log("Cannot get an exclusive lock on file", sourcePath, null, 10);
                            canMove = false;
                            break;
                        }
                    }
                    if (!canMove)
                    {
                        if (!failedBundles.ContainsKey(xmlFileName))
                        {
                            failedBundles.Add(xmlFileName, new FailInfo(true));
                        }
                        continue;
                    }
                    bool moveSucceeded = true;
                    foreach (string file in filesToMove)
                    {
                        string sourcePath = Path.Combine(sourceDir, file);
                        string destPath = Path.Combine(GetDestinationDirectory(file), file);
                        try
                        {
                            File.Move(sourcePath, destPath);
                        }
                        catch (IOException ex)
                        {
                            Log("Failed to move file", sourcePath, ex, 11);
                            moveSucceeded = false;
                            break;
                        }
                        if (shouldResetPermissions)
                        {
                            try
                            {
                                var fileSecurity = new FileSecurity();
                                fileSecurity.SetAccessRuleProtection(false, false);
                                File.SetAccessControl(destPath, fileSecurity);
                            }
                            catch (IOException ex)
                            {
                                Log("Failed to reset permissions", destPath, ex, 20);
                            }
                        }
                    }
                    if (moveSucceeded)
                    {
                        try
                        {
                            Directory.Delete(sourceDir);
                        }
                        catch (IOException ex)
                        {
                            Log("Failed to delete source directory", sourceDir, ex, 22);
                        }
                    }
                    else
                    {
                        if (failedBundles.TryGetValue(xmlFileName, out info))
                        {
                            info.Retry = false;
                        }
                        else
                        {
                            failedBundles.Add(xmlFileName, new FailInfo(false));
                        }
                    }
                }
                if (!stop)
                {
                    Thread.Sleep(5000);
                }               
            }
        }
    }
}
