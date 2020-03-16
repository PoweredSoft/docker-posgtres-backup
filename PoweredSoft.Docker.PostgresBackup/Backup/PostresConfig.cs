using System;
using System.Collections.Generic;
using System.Text;

namespace PoweredSoft.Docker.PostgresBackup.Backup
{
    public class PostresConfiguration
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string User { get; set; }
        public string Password { get; set; }

        public string PgDumpPath { get; set; }
    }
}
