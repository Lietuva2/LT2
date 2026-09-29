using System;
using System.Collections.Generic;
using Data.Enums;
using Framework.Lists;
using PagedList;

namespace Data.ViewModels.Search
{
    public class SearchModel
    {
        public List<SearchIndexModel> List { get; set; }
        public IPagedList<SearchIndexModel> ExpandableList { get; set; }
        public string SearchPhrase { get; set; }
        public string Message { get; set; }

        public SearchModel()
        {
            List = new List<SearchIndexModel>();
        }
    }
}