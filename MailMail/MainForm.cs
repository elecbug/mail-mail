using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.Diagnostics;

using Thread = System.Threading.Thread;

namespace MailMail
{
    public class MainForm : Form
    {
        public MainForm()
        {
            Text = "MailMail - A Simple Mail Client";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(800, 600);
            MinimumSize = new Size(800, 600);

            SettingMail();
        }

        public void SettingMail()
        {
            Thread t = new Thread(() => Gmail.Setup(false).Wait());

            t.Start();
            t.Join(); // Wait for the thread to complete before proceeding
        }
    }
}