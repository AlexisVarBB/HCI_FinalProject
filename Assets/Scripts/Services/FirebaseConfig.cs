namespace DetoxBet.Services
{
    /// <summary>
    /// Datos de tu proyecto de Firebase.
    /// La Web API Key está en Firebase ▸ Configuración del proyecto ▸ General.
    /// No es secreta: la seguridad la dan las reglas de la base de datos.
    /// </summary>
    public static class FirebaseConfig
    {
        public const string ApiKey = "AIzaSyA1bM0w-6pZvVw11JLbM0A5HT2SqG6cep4";
        public const string ProjectId = "detoxbet-32aba";
        public const string AuthDomain = ProjectId + ".firebaseapp.com";
        public const string DatabaseUrl = "https://detoxbet-32aba-default-rtdb.firebaseio.com/";

        /// <summary>Dominio de los correos internos ({llave}@...) que Firebase usa para las contraseñas. No existe en la vida real.</summary>
        public const string UsernameEmailDomain = "users.detoxbet.app";
    }
}
