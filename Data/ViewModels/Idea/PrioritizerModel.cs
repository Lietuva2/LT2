using System.Collections.Generic;

namespace Data.ViewModels.Idea
{
    public class PrioritizerModel
    {
        public List<PrioritizerPair> Items { get; set; }

        public PrioritizerModel()
        {
            Items = new List<PrioritizerPair>();
        }
    }
}