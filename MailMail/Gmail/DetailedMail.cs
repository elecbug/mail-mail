namespace MailMail.Gmail
{
    public sealed class DetailedMail
    {
        public string ID { get; set; } = "";
        public string ThreadID { get; set; } = "";
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string CC { get; set; } = "";
        public string BCC { get; set; } = "";
        public string Subject { get; set; } = "";
        public DateTimeOffset? Date { get; set; }
        public string Snippet { get; set; } = "";
        public string TextBody { get; set; } = "";   // concatenated text/plain parts
        public string HTMLBody { get; set; } = "";   // concatenated text/html parts
        public List<MailAttachment> Attachments { get; set; } = [];
    }

    public sealed class MailAttachment
    {
        public string AttachmentID { get; set; } = "";  // Gmail attachment id for later download
        public string Filename { get; set; } = "";
        public string MimeType { get; set; } = "";
        public long? Size { get; set; }
        public byte[]? Content { get; set; }            // filled only when downloadAttachments=true
    }
}
