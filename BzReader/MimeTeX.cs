using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

namespace BzReader
{
    public static class MimeTeX
    {
        [DllImport("MimeTex.dll")]
        internal static extern int CreateGifFromEq(string expr, string fileName);

        /// <summary>
        /// The handler for the TeX equation requests
        /// </summary>
        /// <param name="sender">Web server instance</param>
        /// <param name="e">URL containing the TeX formula</param>
        public static void WebServer_UrlRequested(object sender, UrlRequestedEventArgs e)
        {
            if (String.IsNullOrEmpty(e.TeXEquation))
            {
                return;
            }
            
            string tempFile = String.Empty;
            byte[] response = new byte[0];

            e.MimeType = "image/gif";
            e.Redirect = false;

            lock (typeof(MimeTeX))
            {
                try
                {
                    tempFile = Path.GetTempFileName();

                    File.Delete(tempFile);

                    CreateGifFromEq(e.TeXEquation, tempFile);

                    response = File.ReadAllBytes(tempFile);
                }
                catch
                {
                }
                finally
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch
                    {
                    }

                    e.Response = response;
                }
            }
        }
    }
}
