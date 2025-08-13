namespace MailMail.Gmail
{
    public sealed class ReceivedMail
    {
        public string ID { get; set; } = "";
        public string From { get; set; } = "";
        public string Subject { get; set; } = "";
        public DateTimeOffset? Date { get; set; }
    }
}
