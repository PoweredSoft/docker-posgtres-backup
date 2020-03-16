using System;
using System.Collections.Generic;
using System.Text;

namespace PoweredSoft.Docker.PostgresBackup.Backup
{
    public class BackupOptions
    {
        public string BasePath { get; set; } = "postgres_backups";
        public string Databases { get; set; } = "*";
    }
}
