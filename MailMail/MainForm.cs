namespace MailMail
{
    public class MainForm : Form
    {
        private MailControl.MailPanel _mailPanel;

        public MainForm()
        {
            Text = "Mail Mail - A Simple Mail Client";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(800, 600);
            MinimumSize = new Size(800, 600);
            
            _mailPanel = new MailControl.MailPanel("user1")
            {
                Parent = this,
                Visible = true,
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                View = View.Details,
            };
        }
    }
}