using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using MimeKit;
using System.Diagnostics;
using Message = Google.Apis.Gmail.v1.Data.Message;

namespace MailMail
{
    internal class Gmail
    {
        public string From { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;

        private static readonly string[] Scopes = new[]
        {
            GmailService.Scope.GmailReadonly, // read messages
            GmailService.Scope.GmailSend      // send messages
        };

        public static async Task Setup(bool forceRelogin)
        {
            try
            {
                using var stream = new FileStream("credentials.json", FileMode.Open, FileAccess.Read);

                var store = new FileDataStore("token.json", true);
                var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(GoogleClientSecrets.FromStream(stream).Secrets, Scopes, "Main Test User", CancellationToken.None, store);

                if (forceRelogin)
                {
                    await store.ClearAsync();
                }

                using var gmailService = new GmailService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "MailMail"
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during mail setup: {ex.Message}");
                throw new Exception("Failed to initialize Gmail service. Please check your credentials and try again.", ex);
            }
        }

        public static async Task<List<Gmail>> ListRecentInboxAsync(GmailService svc, int maxResults = 5, int date = 30)
        {
            if (svc == null)
            {
                throw new ArgumentNullException(nameof(svc));
            }

            var results = new List<Gmail>();
            string? pageToken = null;

            while (results.Count < maxResults)
            {
                var listReq = svc.Users.Messages.List("me");
                listReq.LabelIds = new List<string> { "INBOX" };    // must be a list
                listReq.Q = $"\"newer_than:{date}d\"";             // Gmail search query

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

                    results.Add(new Gmail
                    {
                        From = GetHeader(full, "From"),
                        Subject = GetHeader(full, "Subject"),
                        Date = GetHeader(full, "Date")
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

        public static async Task<bool> SendGmailAsync(GmailService svc, string to, string subject, string text, string? html = null)
        {
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

        private static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static string GetHeader(Message msg, string name)
        {
            var headers = msg.Payload?.Headers;
            var value = headers?.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
            return value ?? string.Empty;
        }
    }
}
