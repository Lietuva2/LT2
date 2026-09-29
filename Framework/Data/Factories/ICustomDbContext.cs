using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Framework.Data.Factories
{
    public interface ICustomDbContext
    {
        ContextHelper Helper { get; set; }
    }
}
