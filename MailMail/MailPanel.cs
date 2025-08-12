using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MailMail
{
    public class MailPanel : ListView
    {
        public string Username { get; private set; }

        public MailPanel(string username) : base()
        {
            Username = username;

            Thread t = new Thread(async () => await UpdateMails());

            t.Start();
            t.Join();

            Columns.Add("Subject", 300, HorizontalAlignment.Left);
            Columns.Add("From", 200, HorizontalAlignment.Left);
            Columns.Add("Date", 150, HorizontalAlignment.Left);

            DoubleClick += MailPanelDoubleClick;
        }

        private async void MailPanelDoubleClick(object? sender, EventArgs e)
        {
            if (SelectedItems.Count == 0)
            {
                return;
            }

            var item = SelectedItems[0];
            var mail = item.Tag as Gmail.Mail;
            
            if (mail == null)
            {
                return;
            }

            var detail = await Gmail.Service.GetMailDetailAsync(Username, mail.ID);

            MessageBox.Show(
                $"From: {detail.From}\n" +
                $"To: {detail.To}\n" +
                $"Subject: {detail.Subject}\n" +
                $"Date: {detail.Date?.ToString("yyyy-MM-dd HH:mm:ss")}\n\n" +
                $"{detail.TextBody}",
                "Mail Detail",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        public async Task UpdateMails()
        {
            await Gmail.Service.Setup(Username);

            var items = await Gmail.Service.GetMailListAsync(Username, 10, 30);

            Invoke(Items.Clear);

            foreach (var mail in items ?? [])
            {
                DateTimeOffset.TryParse(mail.Date, out var dto);

                var item = new ListViewItem(mail.Subject);

                item.SubItems.Add(mail.From);
                item.SubItems.Add(dto.ToString("yyyy-MM-dd HH:mm:ss"));
                item.Tag = mail;

                Invoke(() => Items.Add(item));
            }
        }
    }
}
