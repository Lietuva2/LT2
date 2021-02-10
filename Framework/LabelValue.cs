using System;

namespace Framework
{
    /// <summary>
    /// Class representing text/value pair.
    /// </summary>
    public class LabelValue
    {
        private string val;
        private string _id;
        /// <summary>
        /// Initializes a new instance of the <see cref="TextValue"/> class.
        /// </summary>
        public LabelValue()
        {
        }

        public LabelValue(int value, string text)
        {
            this.value = value.ToString();
            label = text;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TextValue"/> class.
        /// </summary>
        /// <param name="value">The value.</param>
        public LabelValue(object value)
        {
            this.value = value.ToString();
            label = value.ToString();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TextValue"/> class.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="text">The text.</param>
        public LabelValue(object value, string text)
        {
            this.value = value.ToString();
            label = text;
        }

        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        /// <value>The value.</value>
        public string value
        {
            get
            {
                if (!ValueInt.HasValue)
                {
                    return val;
                }
                return ValueInt.ToString();
            }
            set { val = value; }
        }

        public int? ValueInt { get; set; }

        /// <summary>
        /// Gets or sets the text.
        /// </summary>
        /// <value>The text.</value>
        public string label { get; set; }

        public string id
        {
            get
            {
                if (!IdInt.HasValue)
                {
                    return _id;
                }
                return IdInt.ToString();
            }
            set { _id = value; }
        }

        public int? IdInt { get; set; }
    }
}
