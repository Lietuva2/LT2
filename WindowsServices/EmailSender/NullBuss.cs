using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Framework.Bus;


namespace EmailSender
{
    public class NullBus : IBus
    {
        public void Send<T>(T command) where T : Command
        {
            throw new NotImplementedException();
        }
    }
}
