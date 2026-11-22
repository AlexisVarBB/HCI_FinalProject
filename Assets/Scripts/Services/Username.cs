using System.Text;

namespace DetoxBet.Services
{
    /// <summary>
    /// Reglas del nombre de usuario.
    ///
    /// - Lo que el usuario escribe se muestra tal cual ("Jorge Ruiz").
    /// - Para comparar se usa su LLAVE: minúsculas, sin acentos, solo letras y números ("jorgeruiz").
    ///   Dos nombres con la misma llave son el mismo nombre: no puede haber "Jorge" y "jorge".
    /// - La llave se usa en: usernames/{llave} (registro de nombres únicos),
    ///   el correo interno de la contraseña ({llave}@users.detoxbet.app)
    ///   y el token de los Atajos (users/shortcut_events/{llave}).
    /// </summary>
    public static class Username
    {
        public const int MinLength = 2;
        public const int MaxLength = 20;
        public const int MinPasswordLength = 6; // mínimo que exige Firebase

        /// <summary>Llave normalizada. Vacía si no queda ninguna letra o número.</summary>
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

        /// <summary>Correo interno con el que Firebase guarda la contraseña. El usuario nunca lo ve.</summary>
        public static string Email(string name) => $"{Key(name)}@{FirebaseConfig.UsernameEmailDomain}";

        /// <summary>null si el nombre es válido; si no, el mensaje para mostrar.</summary>
        public static string Validate(string name)
        {
            string trimmed = name?.Trim() ?? "";
            if (trimmed.Length < MinLength || trimmed.Length > MaxLength)
                return $"El nombre debe tener entre {MinLength} y {MaxLength} caracteres.";

            foreach (char c in trimmed)
                if (!char.IsLetterOrDigit(c) && c != ' ' && c != '_' && c != '-')
                    return "Usa solo letras, números, espacios, - o _.";

            if (Key(trimmed).Length < MinLength)
                return "El nombre necesita al menos 2 letras o números.";
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
