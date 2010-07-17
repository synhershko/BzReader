using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Globalization;
using System.Threading;

namespace BzReader
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            SetUILanguage();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new BrowseForm());
        }

        /// <summary>
        /// Sets the proper culture for UI
        /// </summary>
        private static void SetUILanguage()
        {
            CultureInfo formattingCulture = Thread.CurrentThread.CurrentCulture;
            CultureInfo uiCulture = Thread.CurrentThread.CurrentUICulture;

            CultureInfo nonEnglishCulture = uiCulture;

            // A lot of people use English Windows even though their native language is different

            if (!formattingCulture.TwoLetterISOLanguageName.Equals("en", StringComparison.InvariantCultureIgnoreCase))
            {
                nonEnglishCulture = formattingCulture;
            }

            Thread.CurrentThread.CurrentCulture = nonEnglishCulture;
            Thread.CurrentThread.CurrentUICulture = nonEnglishCulture;
        }
    }
}