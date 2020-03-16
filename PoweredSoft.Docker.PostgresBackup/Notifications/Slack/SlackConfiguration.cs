using System;
using System.Collections.Generic;
using System.Text;

namespace PoweredSoft.Docker.PostgresBackup.Notifications.Slack
{
    class SlackConfiguration
    {
        public bool Enabled { get; set; } = false;
        public string Webhook { get; set; } = null;
    }
}
