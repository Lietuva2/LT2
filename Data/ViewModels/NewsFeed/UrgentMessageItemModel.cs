using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Data.ViewModels.NewsFeed
{
    public class UrgentMessageItemModel
    {
        public string ObjectId { get; set; }
        public string Subject { get; set; }
        public bool Highlight { get; set; }
        public int? Count { get; set; }
    }
}
