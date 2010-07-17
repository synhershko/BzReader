using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Web;

namespace BzReader
{
    /// <summary>
    /// The delegate for the UrlRequested event
    /// </summary>
    /// <param name="sender">The object which received the web browser request</param>
    /// <param name="e">Url which was requested</param>
    public delegate void UrlRequestedHandler(object sender, UrlRequestedEventArgs e);

    /// <summary>
    /// The class which used to pass information about the requested url to web browser
    /// </summary>
    public class UrlRequestedEventArgs : EventArgs
    {
        /// <summary>
        /// The topic which was request by web browser
        /// </summary>
        private string topicName;
        /// <summary>
        /// The name of the index we're going to request from
        /// </summary>
        private string indexName;
        /// <summary>
        /// The TeX equation embedded into Wikipedia
        /// </summary>
        private string texEquation;
        /// <summary>
        /// Whether this is a redirect response
        /// </summary>
        private bool redirect;
        /// <summary>
        /// The redirect target
        /// </summary>
        private string redirectTarget;
        /// <summary>
        /// The MIME type of the response
        /// </summary>
        private string mimeType;
        /// <summary>
        /// The response string
        /// </summary>
        private byte[] response;

        /// <summary>
        /// The requested topic name
        /// </summary>
        public string TopicName
        {
            get { return topicName; }
        }

        /// <summary>
        /// The name of the index that is to be searched
        /// </summary>
        public string IndexName
        {
            get { return indexName; }
        }

        /// <summary>
        /// Whether this is a redirect response
        /// </summary>
        public bool Redirect
        {
            get { return redirect; }
            set { redirect = value; }
        }

        /// <summary>
        /// Redirection target
        /// </summary>
        public string RedirectTarget
        {
            get { return redirectTarget; }
            set { redirectTarget = value; }
        }

        /// <summary>
        /// The TeX equation embedded into Wikipedia
        /// </summary>
        public string TeXEquation
        {
            get { return texEquation; }
            set { texEquation = value; }
        }

        /// <summary>
        /// Response string
        /// </summary>
        public byte[] Response
        {
            get { return response; }
            set { response = value; }
        }

        /// <summary>
        /// The MIME type of the response
        /// </summary>
        public string MimeType
        {
            get { return mimeType; }
            set { mimeType = value; }
        }

        /// <summary>
        /// The URL that is/was used to fetch this document
        /// </summary>
        public static string Url(string indexName, string topicName, int port)
        {
            return String.Format("http://localhost:{0}/?topic={1}&index={2}", port, HttpUtility.UrlEncode(topicName), HttpUtility.UrlEncode(indexName));
        }

        /// <summary>
        /// The TeX equation URL
        /// </summary>
        /// <param name="equation">Equation text</param>
        /// <param name="port">Port the server is listening on</param>
        /// <returns>Encoded URL</returns>
        public static string TeXUrl(string equation, int port)
        {
            return String.Format("http://localhost:{0}/?tex={1}", port, HttpUtility.UrlEncode(equation));
        }

        /// <summary>
        /// The url to the redirected topic
        /// </summary>
        public string RedirectUrl(int port)
        {
            return String.Format("http://localhost:{0}/?topic={1}&index={2}", port, HttpUtility.UrlEncode(redirectTarget), HttpUtility.UrlEncode(indexName));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UrlRequestedEventArgs"/> class.
        /// </summary>
        /// <param name="aUrl">A URL which was request by web browser.</param>
        public UrlRequestedEventArgs(string aUrl)
            : base()
        {
            NameValueCollection nvc = HttpUtility.ParseQueryString(aUrl.Substring(1));

            topicName = nvc["topic"];
            indexName = nvc["index"];
            texEquation = nvc["tex"];

            if (String.IsNullOrEmpty(topicName))
            {
                topicName = HttpUtility.UrlDecode(aUrl.Substring(1));
            }
        }
    };
}
