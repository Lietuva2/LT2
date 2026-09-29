using System;
using System.Collections.Generic;
using System.Linq;
using System.Resources;
using System.Web.Mvc;

namespace Framework.Mvc.Lists
{
    public class EnumList<T>
    {
        public IList<SelectListItem> Items { get; set; }

        public EnumList(ResourceManager rm)
        {
            Items = new List<SelectListItem>();
            foreach (string val in Enum.GetNames(typeof(T)))
            {
                Items.Add(new SelectListItem()
                {
                    Text = rm != null ? rm.GetString(val) : val,
                    Value = ((int)Enum.Parse(typeof(T), val)).ToString(),
                    Selected = false
                });
            }
        }

        public List<T> GetSelectedItems()
        {
            var list = new List<T>();
            foreach (var item in this.Items)
            {
                if (item.Selected)
                {
                    list.Add((T)Enum.Parse(typeof(T), item.Value));
                }
            }

            return list;
        }

        public void SelectedItems(IEnumerable<T> selectedItems)
        {
            foreach (var item in this.Items)
            {
                if (selectedItems.Contains((T)Enum.Parse(typeof(T), item.Value)))
                {
                    item.Selected = true;
                }
            }
        }

        public EnumList()
            : this(null)
        {
        }
    }
}
