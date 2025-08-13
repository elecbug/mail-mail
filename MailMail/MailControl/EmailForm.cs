using Microsoft.Web.WebView2.WinForms;

namespace MailMail.MailControl
{
    public class EmailForm : Form
    {
        private readonly string DEFAULT_HTML_PATH = Path.Combine(Environment.CurrentDirectory, "temp.html");

        public Gmail.DetailedMail Mail { get; private set; }

        public EmailForm(Gmail.DetailedMail mail)
        {
            Mail = mail ?? throw new ArgumentNullException(nameof(mail));
            FormClosed += (s, e) =>
            {
                File.Delete(DEFAULT_HTML_PATH); // Clean up the temporary file after loading
            };

            if (mail.HTMLBody != null && mail.HTMLBody.Length > 0)
            {
                using (StreamWriter sw = new StreamWriter(DEFAULT_HTML_PATH))
                {
                    sw.WriteLine(Mail.HTMLBody);
                }
            }
            else
            {
                using (StreamWriter sw = new StreamWriter(DEFAULT_HTML_PATH))
                {
                    sw.WriteLine($"<html><body>{TextMailToHTML(Mail)}</body></html>");
                }
            }

            WebView2 webView = new WebView2
            {
                Parent = this,
                Visible = true,
                Dock = DockStyle.Fill,
                Source = new Uri(DEFAULT_HTML_PATH),
            };
        }

        private string TextMailToHTML(Gmail.DetailedMail mail)
        {
            return $"<html><body>{string.Join("", mail.TextBody.Split('\r', '\n').Select(x => $"<p>{x}</p>").ToArray())}</body></html>";
        }
    }
}
