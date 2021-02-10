
namespace Data.ViewModels.Idea
{
    public class PrioritizerResultItemModel
    {
        public string Id { get; set; }
        public string Subject { get; set; }
        public double Percent { get; set; }
        public string PercentString { get; set; }
        public int PriorityNumber { get; set; }
        public int TotalNumber { get; set; }
    }
}