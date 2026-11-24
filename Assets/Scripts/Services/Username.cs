using System.Text;

namespace DetoxBet.Services
{
  
    public static class Username
    {
        public const int MinLength = 2;
        public const int MaxLength = 20;
        public const int MinPasswordLength = 6; 

      
        public static string Key(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var sb = new StringBuilder(name.Length);
            foreach (char raw in name.Trim().ToLowerInvariant())
            {
                char c = RemoveAccent(raw);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
            }
            return sb.ToString();
        }

       
        public static string Email(string name) => $"{Key(name)}@{FirebaseConfig.UsernameEmailDomain}";

       
        public static string Validate(string name)
        {
            string trimmed = name?.Trim() ?? "";
            if (trimmed.Length < MinLength || trimmed.Length > MaxLength)
                return $"The name must have between {MinLength} and {MaxLength} characters.";

            foreach (char c in trimmed)
                if (!char.IsLetterOrDigit(c) && c != ' ' && c != '_' && c != '-')
                    return "Use only letters, numbers, spaces, - or _.";

            if (Key(trimmed).Length < MinLength)
                return "The name must have at least 2 letters or numbers.";
            return null;
        }

        static char RemoveAccent(char c) => c switch
        {
            'á' or 'à' or 'ä' or 'â' => 'a',
            'é' or 'è' or 'ë' or 'ê' => 'e',
            'í' or 'ì' or 'ï' or 'î' => 'i',
            'ó' or 'ò' or 'ö' or 'ô' => 'o',
            'ú' or 'ù' or 'ü' or 'û' => 'u',
            'ñ' => 'n',
            _ => c
        };
    }
}
