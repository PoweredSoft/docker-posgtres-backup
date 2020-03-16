using Ionic.Zip;
using Microsoft.Extensions.Configuration;
using Npgsql;
using PoweredSoft.Docker.PostgresBackup.Notifications;
using PoweredSoft.Storage.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PoweredSoft.Docker.PostgresBackup.Backup
{
    public class BackupTask : ITask
    {
        private readonly INotifyService notifyService;
        private readonly IStorageProvider storageProvider;
        private readonly IConfiguration configuration;
        private readonly BackupOptions backupOptions;
        private readonly PostresConfiguration postresConfiguration;

        public BackupTask(INotifyService notifyService, IStorageProvider storageProvider, IConfiguration configuration, BackupOptions backupOptions, PostresConfiguration postresConfiguration)
        {
            this.notifyService = notifyService;
            this.storageProvider = storageProvider;
            this.configuration = configuration;
            this.backupOptions = backupOptions;
            this.postresConfiguration = postresConfiguration;
        }

        public int Priority { get; } = 1;
        public string Name { get; } = "Postgres Database backup task.";

        protected virtual NpgsqlConnection GetDatabaseConnection()
        {
            var pgCSB = new NpgsqlConnectionStringBuilder();
            pgCSB.Host = postresConfiguration.Host;
            pgCSB.Port = postresConfiguration.Port;
            pgCSB.Username = postresConfiguration.User;
            pgCSB.Password = postresConfiguration.Password;
            pgCSB.CommandTimeout = 120;
            var ret = new NpgsqlConnection(pgCSB.ConnectionString);
            return ret;
        }

        protected virtual List<string> GetDatabaseNames(NpgsqlConnection connection)
        {
            var ret = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT datname FROM pg_database";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        ret.Add((string)reader[0]);
                }
            }

            var systemTables = new List<string>()
            {
                "template0", "template1"
            };

            ret.RemoveAll(t => systemTables.Contains(t));
            return ret;
        }

        public async Task<int> RunAsync()
        {
            // get the mysql connection
            Console.WriteLine("Attempting connection to database...");
            using (var connection = GetDatabaseConnection())
            {
                await connection.OpenAsync();
                Console.WriteLine("Connected to database...");
                Console.WriteLine("Fetching database names to backup...");
                var databaseNames = GetDatabaseNames(connection);

                foreach (var databaseName in databaseNames)
                {
                    if (backupOptions.Databases != "*")
                    {
                        var databasesToBackup = backupOptions.Databases.Split(',');
                        if (!databasesToBackup.Any(t => t.Equals(databaseName, StringComparison.InvariantCultureIgnoreCase)))
                        {
                            Console.WriteLine($"Skipping {databaseName} not part of {backupOptions.Databases}");
                            continue;
                        }
                    }

                    Console.WriteLine($"attempting backup of {databaseName}");
                    var tempFileName = Path.GetTempFileName();
                    var zippedTempFile = Path.GetTempFileName();
                    ExecuteDump(databaseName, tempFileName);

                    using (var zip = new ZipFile())
                    {
                        zip.AddFile(tempFileName, "").FileName = $"{databaseName}.postgres";
                        zip.Save(zippedTempFile);

                        try
                        {
                            File.Delete(tempFileName);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Could not clean up temp file {tempFileName} {ex.Message}");
                        }

                        Console.WriteLine("Succesfully created compressed postgres backup file.");
                    }

                    var destination = $"{backupOptions.BasePath}/{databaseName}_{DateTime.Now:yyyyMMdd_hhmmss_fff}.postgres.zip";
                    using (var fs = new FileStream(zippedTempFile, FileMode.Open, FileAccess.Read))
                    {
                        await storageProvider.WriteFileAsync(fs, destination);
                        Console.WriteLine("Succesfully transfered backup to storage");
                    }

                    try
                    {
                        File.Delete(zippedTempFile);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not clean up temp file {zippedTempFile} {ex.Message}");
                    }
                };
            }

            return 0;
        }

        protected void ExecuteDump(string databaseName, string tempFileName)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ExecuteWindowsDump(databaseName, tempFileName);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                ExecuteLinuxDump(databaseName, tempFileName);
        }

        protected void ExecuteWindowsDump(string databaseName, string tempFileName)
        {
            var dumpCommand = "\"" + postresConfiguration.PgDumpPath + "\"" + " -Fc" + " -h " + postresConfiguration.Host + " -p " +
                              postresConfiguration.Port + " -d " + databaseName + " -U " + postresConfiguration.User + "";
            var passFileContent = "" + postresConfiguration.Host + ":" + postresConfiguration.Port + ":" + databaseName + ":" +
                                  postresConfiguration.User + ":" + postresConfiguration.Password + "";

            var batFilePath = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid() + ".bat");

            var passFilePath = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid() + ".conf");

            try
            {
                var batchContent = "";
                batchContent += "@" + "set PGPASSFILE=" + passFilePath + "\n";
                batchContent += "@" + dumpCommand + "  > " + "\"" + tempFileName + "\"" + "\n";

                File.WriteAllText(
                    passFilePath,
                    passFileContent,
                    Encoding.ASCII);

                File.WriteAllText(
                    batFilePath,
                    batchContent,
                    Encoding.ASCII);

                if (File.Exists(tempFileName))
                    File.Delete(tempFileName);

                var oInfo = new ProcessStartInfo(batFilePath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(oInfo))
                {
                    if (proc == null) return;
                    proc.WaitForExit();
                    proc.Close();
                }
            }
            finally
            {
                if (File.Exists(batFilePath))
                    File.Delete(batFilePath);

                if (File.Exists(passFilePath))
                    File.Delete(passFilePath);
            }
        }

        protected void ExecuteLinuxDump(string databaseName, string tempFileName)
        {
            var command = $"PGPASSWORD=\"{postresConfiguration.Password}\" pg_dump -h {postresConfiguration.Host} -p {postresConfiguration.Port} -U {postresConfiguration.User} -F c -b -v -f \"{tempFileName}\" {databaseName}";

            var result = "";
            using (var proc = new Process())
            {
                proc.StartInfo.FileName = "/bin/bash";
                proc.StartInfo.Arguments = "-c \" " + command + " \"";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.Start();

                result += proc.StandardOutput.ReadToEnd();
                result += proc.StandardError.ReadToEnd();

                Console.WriteLine(result);
                proc.WaitForExit();
            }
        }
    }
}
