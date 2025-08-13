namespace MailMail.Helper
{
    public static class Base64
    {
        public static string ToBase64(byte[] input)
        {
            return Convert.ToBase64String(input).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        public static byte[] FromBase64(string input)
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
    }
}
