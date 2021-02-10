using System;
using System.Collections.Generic;
using Data.Enums;

namespace Data.ViewModels.Search
{
    public class SearchIndexModel
    {
        public string Id { get; set; }
        public string Subject { get; set; }
        public float Score { get; set; }
        public string HighlightedText { get; set; }
        public EntryTypes Type { get; set; }

        public SearchIndexModel()
        {
        }
    }
}