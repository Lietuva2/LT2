using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;

namespace Data.MongoDB
{
    public class Country
    {
        public MongoObjectId Id { get; set; }
        public string Name { get; set; }
        public List<City> Cities { get; set; }

        public Country()
        {
            Cities = new List<City>();
        }
    }

    public class City
    {
        public string Name { get; set; }
    }
}
