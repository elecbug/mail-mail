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

                Service.UserService[username] = gmailService; // Store the service in the static dictionary
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during mail setup: {ex.Message}");
                throw new Exception("Failed to initialize Gmail service. Please check your credentials and try again.", ex);
            }
        }

        public static async Task<List<Mail>> ListRecentInboxAsync(string username, int maxResults = 5, int date = 30)
        {
            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentException("Username cannot be null or empty.", nameof(username));
            }

            if (!Service.UserService.TryGetValue(username, out var svc))
            {
                throw new KeyNotFoundException($"No Gmail service found for user '{username}'. Please ensure you have called Setup() first.");
            }

            if (svc == null)
            {
                throw new ArgumentNullException(nameof(svc));
            }

            var results = new List<Mail>();
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

                    results.Add(new Mail
                    {
                        From = GetHeader(full, "From"),
                        Subject = GetHeader(full, "Subject"),
                        Date = GetHeader(full, "Date"),
                        ID = m.Id ?? string.Empty,
                    });

                    if (results.Count >= maxResults)
                        break; // stop early if we've reached the target
                }

                // Move to next page if available
                pageToken = page.NextPageToken;

                if (string.IsNullOrEmpty(pageToken))
                    break;
            }

            return results;
        }

        public static async Task<MailDetail> GetMessageDetailAsync(string username, string messageId, bool downloadAttachments = false)
        {
            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentException("Username cannot be null or empty.", nameof(username));
            }

            if (!Service.UserService.TryGetValue(username, out var svc))
            {
                throw new KeyNotFoundException($"No Gmail service found for user '{username}'. Please ensure you have called Setup() first.");
            }

            if (svc == null)
            {
                throw new ArgumentNullException(nameof(svc));
            }

            if (string.IsNullOrWhiteSpace(messageId))
            {
                throw new ArgumentException("messageId required.", nameof(messageId));
            }

            // 1) Get full message (headers + structured MIME parts)
            var getReq = svc.Users.Messages.Get("me", messageId);
            getReq.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;

            var msg = await getReq.ExecuteAsync();

            var detail = new MailDetail
            {
                Id = msg.Id,
                ThreadId = msg.ThreadId,
                Snippet = msg.Snippet
            };

            // 2) Headers
            detail.From = GetHeader(msg, "From");
            detail.To = GetHeader(msg, "To");
            detail.Cc = GetHeader(msg, "Cc");
            detail.Bcc = GetHeader(msg, "Bcc");
            detail.Subject = GetHeader(msg, "Subject");

            var dateRaw = GetHeader(msg, "Date");
            if (DateTimeOffset.TryParse(dateRaw, out var dto))
                detail.Date = dto;

            // 3) Traverse MIME tree to collect bodies and attachments metadata
            if (msg.Payload != null)
            {
                TraversePart(msg.Payload, detail, parentMime: msg.Payload.MimeType);
            }

            // 4) Optionally download attachment bytes
            if (downloadAttachments && detail.Attachments.Count > 0)
            {
                foreach (var att in detail.Attachments.Where(a => !string.IsNullOrEmpty(a.AttachmentId)))
                {
                    var a = await svc.Users.Messages.Attachments.Get("me", messageId, att.AttachmentId).ExecuteAsync();
                    if (!string.IsNullOrEmpty(a.Data))
                    {
                        att.Content = FromBase64Url(a.Data);
                    }
                }
            }

            return detail;
        }

        public static async Task<bool> SendGmailAsync(string username, string to, string subject, string text, string? html = null)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(to) || string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("Username, recipient email, and message text cannot be null or empty.");
            }

            if (!Service.UserService.TryGetValue(username, out var svc))
            {
                throw new KeyNotFoundException($"No Gmail service found for user '{username}'. Please ensure you have called Setup() first.");
            }

            var profile = await svc.Users.GetProfile("me").ExecuteAsync();
            var from = profile.EmailAddress;

            var msg = new MimeMessage();

            msg.From.Add(new MailboxAddress(string.Empty, from));
            msg.To.Add(MailboxAddress.Parse(to));
            msg.Subject = subject ?? "";

            var body = new BodyBuilder
            {
                TextBody = text ?? ""
            };

            if (!string.IsNullOrEmpty(html))
            {
                body.HtmlBody = html;
            }

            msg.Body = body.ToMessageBody();
            msg.Prepare(EncodingConstraint.SevenBit); // normalize

            using var ms = new MemoryStream();

            await msg.WriteToAsync(ms);
            ms.Position = 0;

            var raw = Base64UrlEncode(ms.ToArray());
            var gmailMsg = new Message { Raw = raw };
            var sent = await svc.Users.Messages.Send(gmailMsg, "me").ExecuteAsync();

            return !string.IsNullOrEmpty(sent?.Id);
        }
        
        private static void TraversePart(MessagePart part, MailDetail detail, string? parentMime)
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
                var text = Encoding.UTF8.GetString(FromBase64Url(part.Body.Data));
                if (!string.IsNullOrEmpty(text))
                {
                    if (detail.TextBody.Length > 0) detail.TextBody += "\n";
                    detail.TextBody += text;
                }
                return;
            }
            if (mime.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) && part.Body?.Data != null)
            {
                var html = Encoding.UTF8.GetString(FromBase64Url(part.Body.Data));
                if (!string.IsNullOrEmpty(html))
                {
                    // Concatenate if multiple alternative parts exist
                    detail.HtmlBody += html;
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
                    AttachmentId = attId,
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
                    AttachmentId = "", // inline small part; no separate download id
                    Filename = filename,
                    MimeType = mime,
                    Size = part.Body?.Size,
                    Content = FromBase64Url(part.Body?.Data ?? "")
                });
            }
        }

        private static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static byte[] FromBase64Url(string input)
        {
            // Convert Base64Url to standard Base64 by padding and replacing characters
            var base64 = input.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }

        private static string GetHeader(Message msg, string name)
        {
            var headers = msg.Payload?.Headers;
            var value = headers?.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
            return value ?? string.Empty;
        }

    }
}
