namespace MailMail.MailControl
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
            var mail = item.Tag as Gmail.ReceivedMail;
            
            if (mail == null)
            {
                return;
            }

            var detail = await Gmail.Service.GetMailDetailAsync(Username, mail.ID);

            new EmailForm(detail)
            {
                Text = mail.Subject,
                Width = 800,
                Height = 600
            }.ShowDialog(this);
        }

        public async Task UpdateMails()
        {
            await Gmail.Service.Setup(Username);

            var items = await Gmail.Service.GetMailListAsync(Username);

            Invoke(Items.Clear);

            foreach (var mail in items ?? [])
            {
                var item = new ListViewItem(mail.Subject);

                item.SubItems.Add(mail.From);
                item.SubItems.Add(mail.Date.ToString());
                item.Tag = mail;

                Invoke(() => Items.Add(item));
            }
        }
    }
}
