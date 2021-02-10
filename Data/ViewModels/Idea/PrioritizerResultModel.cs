using System.Collections.Generic;

namespace Data.ViewModels.Idea
{
    public class PrioritizerResultModel
    {
        public List<PrioritizerResultItemModel> Items { get; set; }
        public List<PrioritizerResultItemModel> TotalItems { get; set; }

        public PrioritizerResultModel()
        {
            Items = new List<PrioritizerResultItemModel>();
            TotalItems = new List<PrioritizerResultItemModel>();
        }
    }
}