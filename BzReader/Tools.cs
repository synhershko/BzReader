/* This file is taken from ScrewTurn C# wiki. It is released under GPL v2.0. I believe there is no copyright infringement here
 * as I'm going to release my code as GPL as well. */

using System;
using System.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web;
using System.Globalization;

namespace ScrewTurn.Wiki
{
    /// <summary>
    /// Contains useful Tools.
    /// </summary>
    public static class Tools
    {
		/// <summary>
		/// Executes URL-encoding, avoiding to use '+' for spaces.
		/// </summary>
		/// <remarks>This method uses internally Server.UrlEncode.</remarks>
		/// <param name="input">The input string.</param>
		/// <returns>The encoded string.</returns>
		public static string UrlEncode(string input) {
			return HttpUtility.UrlEncode(input).Replace("+", "%20");
		}

        /// <summary>
        /// Executes URL-encoding, avoiding to use '+' for spaces.
        /// </summary>
        /// <remarks>This method uses internally Server.UrlEncode.</remarks>
        /// <param name="input">The input string.</param>
        /// <returns>The encoded string.</returns>
        public static string UrlDecode(string input)
        {
            return HttpUtility.UrlDecode(input);
        }
	}
}
