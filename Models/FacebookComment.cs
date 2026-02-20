namespace Facebook_Assistent.Models
{
    public class FacebookComment
    {
        public string AuthorName { get; set; }
        public string Message { get; set; }
        public string CreatedAt { get; set; }

        public string PreviewText => $"{AuthorName} ({CreatedAt}): {Message}";
    }
}
