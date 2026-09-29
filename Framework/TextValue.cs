using System;

namespace Framework
{
    /// <summary>
    /// Class representing text/value pair.
    /// </summary>
    public class TextValue
    {
        private string val;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextValue"/> class.
        /// </summary>
        public TextValue()
        {
        }

        public TextValue(int value, string text)
        {
            Value = value.ToString();
            Text = text;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TextValue"/> class.
        /// </summary>
        /// <param name="value">The value.</param>
        public TextValue(object value)
        {
            Value = value.ToString();
            Text = value.ToString();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TextValue"/> class.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="text">The text.</param>
        public TextValue(object value, string text)
        {
            Value = value.ToString();
            Text = text;
        }

        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        /// <value>The value.</value>
        public string Value
        {
            get { return val; }
            set { val = value; }
        }

        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        /// <value>The value.</value>
        public int ValueInt
        {
            get { return Convert.ToInt32(val); }
            set { val = value.ToString(); }
        }

        /// <summary>
        /// Gets or sets the text.
        /// </summary>
        /// <value>The text.</value>
        public string Text { get; set; }
    }
}
