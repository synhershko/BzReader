using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web;

namespace BzReader
{
    /// <summary>
    /// Serves the HTTP content back to IE control
    /// </summary>
    internal class WebServer
    {
        /// <summary>
        /// The port we're listening on
        /// </summary>
        private int port;
        /// <summary>
        /// The TCP listener
        /// </summary>
        private TcpListener listener;
        /// <summary>
        /// The list of the currently executing threads
        /// </summary>
        private List<Thread> threads = new List<Thread>();
        /// <summary>
        /// Happens when the browser requests a resource
        /// </summary>
        public event UrlRequestedHandler UrlRequested;
        /// <summary>
        /// Singleton stuff
        /// </summary>
        private static WebServer instance;

        /// <summary>
        /// Singleton stuff
        /// </summary>
        public static WebServer Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new WebServer();
                }

                return instance;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WebServer"/> class.
        /// </summary>
        private WebServer()
        {
            // Hook up TeX renderer in here

            this.UrlRequested += new UrlRequestedHandler(MimeTeX.WebServer_UrlRequested);

            listener = new TcpListener(IPAddress.Loopback, 0);

            listener.Start();

            port = ((IPEndPoint)listener.LocalEndpoint).Port;

            Thread t = new Thread(ProcessRequests);

            threads.Add(t);

            t.Start();
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public void Stop()
        {
            listener.Stop();

            lock (this)
            {
                foreach (Thread t in threads)
                {
                    if (t.IsAlive)
                    {
                        t.Abort();
                    }
                }
            }
        }

        /// <summary>
        /// Generates the URL for the given request term
        /// </summary>
        /// <param name="term">The request term to generate the URL for</param>
        /// <returns>The URL</returns>
        public string GenerateUrl(PageInfo page)
        {
            return UrlRequestedEventArgs.Url(page.Indexer.File, page.Name, port);
        }

        /// <summary>
        /// Generates a TeX equation URL using the provided equation text
        /// </summary>
        /// <param name="equation">Equation text</param>
        /// <returns>TeX equation image URL</returns>
        public string GenerateTeXUrl(string equation)
        {
            return UrlRequestedEventArgs.TeXUrl(equation, port);
        }

        /// <summary>
        /// Sends HTTP header to the client
        /// </summary>
        /// <param name="httpVersion">Http version string</param>
        /// <param name="bytesCount">The number of bytes in the response stream</param>
        /// <param name="statusCode">HTTP status code</param>
        /// <param name="socket">The socket where to write to</param>
        /// <param name="mime">The MIME type of the response</param>
        /// <param name="redirectLocation">The location to redirect the browser to</param>
        public void SendHeader(string httpVersion, int bytesCount, string redirectLocation, Socket socket, string mime)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(httpVersion);
            sb.Append(" ");
            sb.Append(String.IsNullOrEmpty(redirectLocation) ? "200" : "302");
            sb.AppendLine();
            sb.Append("Content-Type: ");
            sb.AppendLine(mime);
            
            if (!String.IsNullOrEmpty(redirectLocation))
            {
                sb.Append("Location: ");
                sb.AppendLine(redirectLocation);
            }

            sb.AppendLine("Accept-Ranges: bytes");
            sb.Append("Content-Length: ");
            sb.Append(bytesCount);
            sb.AppendLine();
            sb.AppendLine();

            socket.Send(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        /// <summary>
        /// Processes requests coming from the web browser
        /// </summary>
        public void ProcessRequests()
        {
            Socket socket = null;

            try
            {
                socket = listener.AcceptSocket();

                Thread t = new Thread(ProcessRequests);

                lock (this)
                {
                    threads.Add(t);
                }

                t.Start();

                if (socket.Connected)
                {
                    socket.ReceiveTimeout = 200;
                    socket.SendTimeout = 200;
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);

                    byte[] buf = new byte[2048];
                    int receivedLen = 0;

                    while ((receivedLen = socket.Receive(buf)) > 0)
                    {
                        string[] requestStrings = Encoding.ASCII.GetString(buf, 0, receivedLen).Split('\r', '\n');

                        if (requestStrings.Length < 2)
                        {
                            throw new Exception(Properties.Resources.HTTPRequestTooFewStrings);
                        }

                        string[] requestParts = requestStrings[0].Split(' ');

                        if (requestParts.Length != 3)
                        {
                            throw new Exception(Properties.Resources.HTTPRequestNoStringParts);
                        }

                        if (!String.Equals(requestParts[0], "GET"))
                        {
                            throw new Exception(String.Format(Properties.Resources.UnknownHTTPRequestType, requestParts[0]));
                        }

                        string httpVersion = requestParts[2];
                        string url = requestParts[1];

                        byte[] response = new byte[0];
                        string redirectUrl = String.Empty;
                        string mime = "text/html";

                        if (UrlRequested != null)
                        {
                            UrlRequestedEventArgs urea = new UrlRequestedEventArgs(url);

                            UrlRequested(this, urea);

                            redirectUrl = urea.Redirect ? urea.RedirectUrl(port) : String.Empty;

                            if (urea.Response != null)
                            {
                                response = urea.Response;
                            }

                            mime = urea.MimeType;
                        }

                        SendHeader(httpVersion, response.Length, redirectUrl, socket, mime);

                        socket.Send(response);
                    }

                    lock (this)
                    {
                        threads.Remove(Thread.CurrentThread);
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                try
                {
                    if (socket != null)
                    {
                        socket.Shutdown(SocketShutdown.Both);

                        socket.Close(0);
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
