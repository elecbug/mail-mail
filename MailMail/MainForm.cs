using MailMail.Gmail;
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

            TestSettingMail();
        }

        public void TestSettingMail()
        {
            Thread t = new Thread(async () => 
            { 
                await Service.Setup("user22", false);

                var results = await Service.ListRecentInboxAsync("user22");

                if (results != null && results.Count > 0)
                {
                    foreach (var mail in results)
                    {
                        Debug.WriteLine($"From: {mail.From}, Subject: {mail.Subject}, Date: {mail.Date}");
                    }

                    var value = await Service.GetMessageDetailAsync("user22", results[0].ID);

                    if (value != null)
                    {
                        Debug.WriteLine($"Mail ID: {value.Id}, Subject: {value.Subject}, From: {value.From}");
                        Debug.WriteLine($"Text Body: {value.TextBody}");
                        Debug.WriteLine($"HTML Body: {value.HtmlBody}");
                    }
                    else
                    {
                        Debug.WriteLine("Failed to retrieve mail details.");
                    }
                }
                else
                {
                    Debug.WriteLine("No recent emails found.");
                }
            });

            t.Start();
            t.Join(); // Wait for the thread to complete before proceeding
        }
    }
}