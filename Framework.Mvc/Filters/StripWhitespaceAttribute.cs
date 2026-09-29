using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;

namespace Framework.Mvc.Filters
{
    /// <summary>
    /// Strips whitespaces from the HTML markup.
    /// </summary>
    public class StripWhitespaceAttribute : ActionFilterAttribute
    {
        public bool Prevent { get; set; }
        /// <summary>
        /// Called by the MVC framework before the action method executes.
        /// </summary>
        /// <param name="filterContext">The filter context.</param>
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var action = filterContext.ActionDescriptor;
            if (action.IsDefined(typeof (IgnoreStripWhitespaceAttribute), true))
            {
                base.OnActionExecuting(filterContext);
                return;
            }

            var controller = action.ControllerDescriptor;
            if (controller.IsDefined(typeof(IgnoreStripWhitespaceAttribute), true))
            {
                base.OnActionExecuting(filterContext); 
                return;
            }

            if (!HttpContext.Current.IsDebuggingEnabled)
            {
                var response = filterContext.HttpContext.Response;
                response.Filter = new WhitespaceFilter(response.Filter);
            }

            base.OnActionExecuting(filterContext);
        }

        /// <summary>
        /// The whitespace filter.
        /// </summary>
        private class WhitespaceFilter : Stream
        {
            /// <summary>
            /// Whitespace removal regular expression.
            /// </summary>
            private static readonly Regex regex = new Regex(@"(?<=[^])\t{2,}|(?<=[>])\s{2,}(?=[<])|(?<=[>])\s{2,11}(?=[<])|(?=[\n])\s{2,}");

            /// <summary>
            /// The input stream.
            /// </summary>
            private readonly Stream stream;

            /// <summary>
            /// Initializes a new instance of the <see cref="WhitespaceFilter"/> class.
            /// </summary>
            /// <param name="stream">The stream.</param>
            public WhitespaceFilter(Stream stream)
            {
                this.stream = stream;
            }

            /// <summary>
            /// When overridden in a derived class, gets a value indicating whether the current stream supports reading.
            /// </summary>
            /// <value></value>
            /// <returns>true if the stream supports reading; otherwise, false.
            /// </returns>
            public override bool CanRead
            {
                get { return true; }
            }

            /// <summary>
            /// When overridden in a derived class, gets a value indicating whether the current stream supports seeking.
            /// </summary>
            /// <value></value>
            /// <returns>true if the stream supports seeking; otherwise, false.
            /// </returns>
            public override bool CanSeek
            {
                get { return true; }
            }

            /// <summary>
            /// When overridden in a derived class, gets a value indicating whether the current stream supports writing.
            /// </summary>
            /// <value></value>
            /// <returns>true if the stream supports writing; otherwise, false.
            /// </returns>
            public override bool CanWrite
            {
                get { return true; }
            }

            /// <summary>
            /// When overridden in a derived class, clears all buffers for this stream and causes any buffered data to be written to the underlying device.
            /// </summary>
            /// <exception cref="T:System.IO.IOException">
            /// An I/O error occurs.
            /// </exception>
            public override void Flush()
            {
                stream.Flush();
            }

            /// <summary>
            /// When overridden in a derived class, gets the length in bytes of the stream.
            /// </summary>
            /// <value></value>
            /// <returns>
            /// A long value representing the length of the stream in bytes.
            /// </returns>
            /// <exception cref="T:System.NotSupportedException">
            /// A class derived from Stream does not support seeking.
            /// </exception>
            /// <exception cref="T:System.ObjectDisposedException">
            /// Methods were called after the stream was closed.
            /// </exception>
            public override long Length
            {
                get { return 0; }
            }

            /// <summary>
            /// When overridden in a derived class, gets or sets the position within the current stream.
            /// </summary>
            /// <value></value>
            /// <returns>
            /// The current position within the stream.
            /// </returns>
            /// <exception cref="T:System.IO.IOException">
            /// An I/O error occurs.
            /// </exception>
            /// <exception cref="T:System.NotSupportedException">
            /// The stream does not support seeking.
            /// </exception>
            /// <exception cref="T:System.ObjectDisposedException">
            /// Methods were called after the stream was closed.
            /// </exception>
            public override long Position { get; set; }

            /// <summary>
            /// When overridden in a derived class, reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
            /// </summary>
            /// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between <paramref name="offset"/> and (<paramref name="offset"/> + <paramref name="count"/> - 1) replaced by the bytes read from the current source.</param>
            /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin storing the data read from the current stream.</param>
            /// <param name="count">The maximum number of bytes to be read from the current stream.</param>
            /// <returns>
            /// The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero (0) if the end of the stream has been reached.
            /// </returns>
            /// <exception cref="T:System.ArgumentException">
            /// The sum of <paramref name="offset"/> and <paramref name="count"/> is larger than the buffer length.
            /// </exception>
            /// <exception cref="T:System.ArgumentNullException">
            /// 	<paramref name="buffer"/> is null.
            /// </exception>
            /// <exception cref="T:System.ArgumentOutOfRangeException">
            /// 	<paramref name="offset"/> or <paramref name="count"/> is negative.
            /// </exception>
            /// <exception cref="T:System.IO.IOException">
            /// An I/O error occurs.
            /// </exception>
            /// <exception cref="T:System.NotSupportedException">
            /// The stream does not support reading.
            /// </exception>
            /// <exception cref="T:System.ObjectDisposedException">
            /// Methods were called after the stream was closed.
            /// </exception>
            public override int Read(byte[] buffer, int offset, int count)
            {
                return stream.Read(buffer, offset, count);
            }

            /// <summary>
            /// When overridden in a derived class, sets the position within the current stream.
            /// </summary>
            /// <param name="offset">A byte offset relative to the <paramref name="origin"/> parameter.</param>
            /// <param name="origin">A value of type <see cref="T:System.IO.SeekOrigin"/> indicating the reference point used to obtain the new position.</param>
            /// <returns>
            /// The new position within the current stream.
            /// </returns>
            /// <exception cref="T:System.IO.IOException">
            /// An I/O error occurs.
            /// </exception>
            /// <exception cref="T:System.NotSupportedException">
            /// The stream does not support seeking, such as if the stream is constructed from a pipe or console output.
            /// </exception>
            /// <exception cref="T:System.ObjectDisposedException">
            /// Methods were called after the stream was closed.
            /// </exception>
            public override long Seek(long offset, SeekOrigin origin)
            {
                return stream.Seek(offset, origin);
            }

            /// <summary>
            /// When overridden in a derived class, sets the length of the current stream.
            /// </summary>
            /// <param name="value">The desired length of the current stream in bytes.</param>
            /// <exception cref="T:System.IO.IOException">
            /// An I/O error occurs.
            /// </exception>
            /// <exception cref="T:System.NotSupportedException">
            /// The stream does not support both writing and seeking, such as if the stream is constructed from a pipe or console output.
            /// </exception>
            /// <exception cref="T:System.ObjectDisposedException">
            /// Methods were called after the stream was closed.
            /// </exception>
            public override void SetLength(long value)
            {
                stream.SetLength(value);
            }

            /// <summary>
            /// Closes the current stream and releases any resources (such as sockets and file handles) associated with the current stream.
            /// </summary>
            public override void Close()
            {
                stream.Close();
            }

            /// <summary>
            /// When overridden in a derived class, writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
            /// </summary>
            /// <param name="buffer">An array of bytes. This method copies <paramref name="count"/> bytes from <paramref name="buffer"/> to the current stream.</param>
            /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin copying bytes to the current stream.</param>
            /// <param name="count">The number of bytes to be written to the current stream.</param>
            /// <exception cref="T:System.ArgumentException">
            /// The sum of <paramref name="offset"/> and <paramref name="count"/> is greater than the buffer length.
            /// </exception>
            /// <exception cref="T:System.ArgumentNullException">
            /// 	<paramref name="buffer"/> is null.
            /// </exception>
            /// <exception cref="T:System.ArgumentOutOfRangeException">
            /// 	<paramref name="offset"/> or <paramref name="count"/> is negative.
            /// </exception>
            /// <exception cref="T:System.IO.IOException">
            /// An I/O error occurs.
            /// </exception>
            /// <exception cref="T:System.NotSupportedException">
            /// The stream does not support writing.
            /// </exception>
            /// <exception cref="T:System.ObjectDisposedException">
            /// Methods were called after the stream was closed.
            /// </exception>
            public override void Write(byte[] buffer, int offset, int count)
            {
                var data = new byte[count];
                Buffer.BlockCopy(buffer, offset, data, 0, count);
                var html = Encoding.Default.GetString(buffer);

                html = regex.Replace(html, string.Empty);

                byte[] outdata = Encoding.Default.GetBytes(html);
                stream.Write(outdata, 0, outdata.GetLength(0));
            }
        }
    }
}
