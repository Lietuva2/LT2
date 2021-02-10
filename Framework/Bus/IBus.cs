using System.Threading.Tasks;

namespace Framework.Bus
{
    public interface IBus
    {
        void Send<T>(T command) where T : Command;
    }
}
