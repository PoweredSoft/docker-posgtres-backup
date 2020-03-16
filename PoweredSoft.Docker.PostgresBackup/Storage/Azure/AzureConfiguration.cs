using System;
using System.Collections.Generic;
using System.Text;

namespace PoweredSoft.Docker.PostgresBackup.Storage.Azure
{
    public class AzureConfiguration
    {
        public bool Enabled { get; set; } = false;
        public string ConnectionString { get; set; }
        public string ContainerName { get; set; }
    }
}
