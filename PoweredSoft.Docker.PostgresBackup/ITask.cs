using System.Threading.Tasks;

namespace PoweredSoft.Docker.PostgresBackup
{
    public interface ITask
    {
        int Priority { get; }

        string Name { get; }

        Task<int> RunAsync();
    }
}
