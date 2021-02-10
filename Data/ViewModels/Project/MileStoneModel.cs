using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Hyper.ComponentModel;

namespace Data.ViewModels.Project
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class MileStoneModel
    {
        public string ProjectId { get; set; }
        public string MileStoneId { get; set; }
        public string Subject { get; set; }
        public DateTime? Date { get; set; }
        public List<ToDoModel> ToDos { get; set; }
        public List<ToDoModel> FinishedToDos { get; set; }
        public bool IsLate { get; set; }
        public bool IsEditable { get; set; }
        public bool IsFinished { get; set; }

        public MileStoneModel()
        {
            IsLate = false;
        }
    }
}
