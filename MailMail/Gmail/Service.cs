using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using MimeKit;
using System.Diagnostics;
using System.Text;

using Message = Google.Apis.Gmail.v1.Data.Message;
using MessagePart = Google.Apis.Gmail.v1.Data.MessagePart;

namespace MailMail.Gmail
{
    public static class Service
    {
        public static Dictionary<string, GmailService> UserService { get; private set; } = new Dictionary<string, GmailService>();

        private static readonly string[] Scopes = 
        [
            GmailService.Scope.GmailReadonly, // read messages
            GmailService.Scope.GmailSend      // send messages
        ];

        public static async Task Setup(string username, bool forceRelogin = false)
        {
            try
            {
                var stream = new FileStream("credentials.json", FileMode.Open, FileAccess.Read);

                var store = new FileDataStore($"{username}_token.json", true);
                var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(GoogleClientSecrets.FromStream(stream).Secrets, Scopes, username, CancellationToken.None, store);

                if (forceRelogin)
                {
                    await store.ClearAsync();
                }

                var gmailService = new GmailService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "MailMail"
                });

                UserService[username] = gmailService; // Store the service in the static dictionary
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during mail setup: {ex.Message}");

                throw new Exception("Failed to initialize Gmail service. Please check your credentials and try again.", ex);
            }
        }

        public static async Task<List<ReceivedMail>> GetMailListAsync(string username, int maxResults = 20, int date = 90)
        {
            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentException("Username cannot be null or empty.", nameof(username));
            }

            if (!UserService.TryGetValue(username, out var svc))
            {
                throw new KeyNotFoundException($"No Gmail service found for user '{username}'. Please ensure you have called Setup() first.");
            }

            if (svc == null)
            {
                throw new ArgumentNullException(nameof(svc));
            }

            var results = new List<ReceivedMail>();
            string? pageToken = null;

            while (results.Count < maxResults)
            {
                var listReq = svc.Users.Messages.List("me");
                listReq.LabelIds = new List<string> { "INBOX" };    // must be a list
                listReq.Q = $"newer_than:{date}d";                  // Gmail search query

                int need = maxResults - results.Count;              // how many still needed
                listReq.MaxResults = need;                          // request only what we need (<= 500 per page)

                if (!string.IsNullOrEmpty(pageToken))
                {
                    listReq.PageToken = pageToken;
                }

                ListMessagesResponse page;

                try
                {
                    page = await listReq.ExecuteAsync();
                }
                catch (Google.GoogleApiException ex)
                {
                    throw new Exception($"Messages.List failed: {ex.Error?.Message ?? ex.Message}", ex);
                }

                if (page.Messages == null || page.Messages.Count == 0)
                {
                    break; // no more matches
                }

                foreach (var m in page.Messages)
                {
                    var getReq = svc.Users.Messages.Get("me", m.Id);

                    getReq.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                    getReq.MetadataHeaders = new[] { "From", "Subject", "Date" };

                    var full = await getReq.ExecuteAsync();
                    var dateRaw = GetHeader(full, "Date");

                    if (RFC2822TimeConverter.TryParseRfc2822Like(dateRaw, out var dto))
                    {
                        // Convert to DateTimeOffset with local timezone
                        dto = RFC2822TimeConverter.ConvertToTimeZone(dto, "Korea Standard Time");
                    }

                    results.Add(new ReceivedMail
                    {
                        From = GetHeader(full, "From"),
                        Subject = GetHeader(full, "Subject"),
                        Date = dto,
                        ID = m.Id ?? string.Empty,
                    });

                    if (results.Count >= maxResults)
                    {
                        break; // stop early if we've reached the target
                    }
                }

                // Move to next page if available
                pageToken = page.NextPageToken;

                if (string.IsNullOrEmpty(pageToken))
                {
                    break;
                }
            }

            return results;
        }

        public static async Task<DetailedMail> GetMailDetailAsync(string username, string messageID, bool downloadAttachments = false)
        {
            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentException("Username cannot be null or empty.", nameof(username));
            }

            if (!UserService.TryGetValue(username, out var svc))
            {
                throw new KeyNotFoundException($"No Gmail service found for user '{username}'. Please ensure you have called Setup() first.");
            }

            if (svc == null)
            {
                throw new ArgumentNullException(nameof(svc));
            }

            if (string.IsNullOrWhiteSpace(messageID))
            {
                throw new ArgumentException("messageId required.", nameof(messageID));
            }

            // 1) Get full message (headers + structured MIME parts)
            var getReq = svc.Users.Messages.Get("me", messageID);
            getReq.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;

            var msg = await getReq.ExecuteAsync();

            var detail = new DetailedMail
            {
                ID = msg.Id,
                ThreadID = msg.ThreadId,
                Snippet = msg.Snippet
            };

            // 2) Headers
            detail.From = GetHeader(msg, "From");
            detail.To = GetHeader(msg, "To");
            detail.CC = GetHeader(msg, "Cc");
            detail.BCC = GetHeader(msg, "Bcc");
            detail.Subject = GetHeader(msg, "Subject");

            var dateRaw = GetHeader(msg, "Date");

            Debug.WriteLine($"Raw Date Header: {dateRaw}");

            if (RFC2822TimeConverter.TryParseRfc2822Like(dateRaw, out var dto))
            {
                dto = RFC2822TimeConverter.ConvertToTimeZone(dto, "Korea Standard Time");
                detail.Date = dto;
            }

            // 3) Traverse MIME tree to collect bodies and attachments metadata
            if (msg.Payload != null)
            {
                TraversePart(msg.Payload, detail, parentMime: msg.Payload.MimeType);
            }

            // 4) Optionally download attachment bytes
            if (downloadAttachments && detail.Attachments.Count > 0)
            {
                foreach (var att in detail.Attachments.Where(a => !string.IsNullOrEmpty(a.AttachmentID)))
                {
                    var a = await svc.Users.Messages.Attachments.Get("me", messageID, att.AttachmentID).ExecuteAsync();
                    if (!string.IsNullOrEmpty(a.Data))
                    {
                        att.Content = Helper.Base64.FromBase64(a.Data);
                    }
                }
            }

            return detail;
        }

        public static async Task<bool> SendMailAsync(string username, SentMail mail)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(mail.To) || string.IsNullOrEmpty(mail.Text))
            {
                throw new ArgumentException("Username, recipient email, and message text cannot be null or empty.");
            }

            if (!UserService.TryGetValue(username, out var svc))
            {
                throw new KeyNotFoundException($"No Gmail service found for user '{username}'. Please ensure you have called Setup() first.");
            }

            var profile = await svc.Users.GetProfile("me").ExecuteAsync();
            var from = profile.EmailAddress;

            var msg = new MimeMessage();

            msg.From.Add(new MailboxAddress(string.Empty, from));
            msg.To.Add(MailboxAddress.Parse(mail.To));
            msg.Subject = mail.Subject ?? "";

            var body = new BodyBuilder
            {
                TextBody = mail.Text ?? ""
            };

            if (!string.IsNullOrEmpty(mail.HTML))
            {
                body.HtmlBody = mail.HTML;
            }

            msg.Body = body.ToMessageBody();
            msg.Prepare(EncodingConstraint.SevenBit); // normalize

            using var ms = new MemoryStream();

            await msg.WriteToAsync(ms);
            ms.Position = 0;

            var raw = Helper.Base64.ToBase64(ms.ToArray());
            var gmailMsg = new Message { Raw = raw };
            var sent = await svc.Users.Messages.Send(gmailMsg, "me").ExecuteAsync();

            return !string.IsNullOrEmpty(sent?.Id);
        }
        
        private static void TraversePart(MessagePart part, DetailedMail detail, string? parentMime)
        {
            if (part == null) return;

            // Multipart container
            if (part.Parts != null && part.Parts.Count > 0)
            {
                foreach (var child in part.Parts)
                    TraversePart(child, detail, part.MimeType);
                return;
            }

            // Leaf part
            var mime = part.MimeType ?? parentMime ?? "";

            // text/plain or text/html bodies
            if (mime.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase) && part.Body?.Data != null)
            {
                var text = Encoding.UTF8.GetString(Helper.Base64.FromBase64(part.Body.Data));
                if (!string.IsNullOrEmpty(text))
                {
                    if (detail.TextBody.Length > 0) detail.TextBody += "\n";
                    detail.TextBody += text;
                }
                return;
            }
            if (mime.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) && part.Body?.Data != null)
            {
                var html = Encoding.UTF8.GetString(Helper.Base64.FromBase64(part.Body.Data));
                if (!string.IsNullOrEmpty(html))
                {
                    // Concatenate if multiple alternative parts exist
                    detail.HTMLBody += html;
                }
                return;
            }

            // Attachment (filename present or attachmentId exists)
            var filename = part.Filename ?? "";
            var attId = part.Body?.AttachmentId ?? "";

            if (!string.IsNullOrEmpty(filename) || !string.IsNullOrEmpty(attId))
            {
                detail.Attachments.Add(new MailAttachment
                {
                    AttachmentID = attId,
                    Filename = filename,
                    MimeType = mime,
                    Size = part.Body?.Size
                });
                return;
            }

            // Some providers embed inline images as 'image/*' with body.data (small) instead of attachmentId.
            // If you want those bytes immediately:
            if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && part.Body?.Data != null)
            {
                detail.Attachments.Add(new MailAttachment
                {
                    AttachmentID = "", // inline small part; no separate download id
                    Filename = filename,
                    MimeType = mime,
                    Size = part.Body?.Size,
                    Content = Helper.Base64.FromBase64(part.Body?.Data ?? "")
                });
            }
        }

        private static string GetHeader(Message msg, string name)
        {
            var headers = msg.Payload?.Headers;
            var value = headers?.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

            return value ?? string.Empty;
        }
    }
}
