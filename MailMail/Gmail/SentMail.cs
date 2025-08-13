namespace MailMail.Gmail
{
    public sealed class SentMail
    {
        public string To { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Text { get; set; } = "";
        public string? HTML { get; set; } = "";
    }
}
