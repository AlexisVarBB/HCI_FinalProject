using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DetoxBet.Services;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    public enum AppScreen { Login, Menu, CreateRoom, JoinRoom, Room, Final }

    /// <summary>
    /// Único punto de navegación de la app. Usa un solo UIDocument: al cambiar de pantalla
    /// borra el árbol actual, carga el UXML de la nueva y le pasa el control a su controlador.
    ///
    /// Flujo:
    ///   Login → Menu ─┬─ CreateRoom ─┐
    ///                 └─ JoinRoom ───┴→ Room → Final → Menu
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ScreenManager : MonoBehaviour
    {
        [Header("UXML de cada pantalla")]
        [SerializeField] VisualTreeAsset loginScreen;
        [SerializeField] VisualTreeAsset menuScreen;
        [SerializeField] VisualTreeAsset createRoomScreen;
        [SerializeField] VisualTreeAsset joinRoomScreen;
        [SerializeField] VisualTreeAsset roomScreen;
        [SerializeField] VisualTreeAsset finalScreen;

        [Header("Marco (limita el ancho en web, tablet o ventana horizontal)")]
        [Tooltip("AppFrame.uss: centra la app y le pone un ancho máximo tipo teléfono.")]
        [SerializeField] StyleSheet frameStyle;

        [Header("Escritorio")]
        [Tooltip("Responsive.uss: reglas para pantallas anchas (clase app-frame--wide).")]
        [SerializeField] StyleSheet responsiveStyle;
        [Tooltip("Ancho (en px de la UI) a partir del cual se usa el diseño de escritorio.")]
        [SerializeField] float wideBreakpoint = 900f;

        [Header("Inicio")]
        [SerializeField] AppScreen firstScreen = AppScreen.Login;

        readonly Dictionary<AppScreen, (VisualTreeAsset uxml, ScreenController controller)> m_Screens = new();

        UIDocument m_Document;
        ScreenController m_Current;
        VisualElement m_Frame;
        bool m_Wide;

        public AppScreen CurrentScreen { get; private set; }

        void Awake()
        {
            m_Document = GetComponent<UIDocument>();

            var login = new LoginController();
            var menu = new MenuController();
            var createRoom = new CreateRoomController();
            var joinRoom = new JoinRoomController();
            var room = new RoomController();
            var final = new FinalController();

            m_Screens[AppScreen.Login] = (loginScreen, login);
            m_Screens[AppScreen.Menu] = (menuScreen, menu);
            m_Screens[AppScreen.CreateRoom] = (createRoomScreen, createRoom);
            m_Screens[AppScreen.JoinRoom] = (joinRoomScreen, joinRoom);
            m_Screens[AppScreen.Room] = (roomScreen, room);
            m_Screens[AppScreen.Final] = (finalScreen, final);

            // ===== Todo el flujo de la app, en un solo lugar =====
            login.onLoggedIn += _ => ResumeOrGoToMenu();

            menu.onLoggedOut += () => GoTo(AppScreen.Login);
            menu.onCreateRoom += () => GoTo(AppScreen.CreateRoom);
            menu.onJoinRoom += () => GoTo(AppScreen.JoinRoom);

            createRoom.onBack += () => GoTo(AppScreen.Menu);
            createRoom.onRoomCreated += _ => GoTo(AppScreen.Room);

            joinRoom.onBack += () => GoTo(AppScreen.Menu);
            joinRoom.onRoomJoined += _ => GoTo(AppScreen.Room);

            room.onChallengeFinished += _ => GoTo(AppScreen.Final);

            final.onNewChallenge += () => GoTo(AppScreen.Menu);
        }

        bool m_Started;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Plugins/WebGL/DetoxVisibility.jslib
        [DllImport("__Internal")]
        static extern void DetoxRegisterVisibility(string gameObjectName, string methodName);
#endif

        void Start()
        {
            // En Start el UIDocument ya creó su árbol raíz.
            m_Started = true;
            Show(firstScreen);

#if UNITY_WEBGL && !UNITY_EDITOR
            // En el navegador no existe OnApplicationPause: se usa el evento de visibilidad de la pestaña.
            DetoxRegisterVisibility(gameObject.name, nameof(OnVisibilityChanged));
#endif
        }

        // ---------- Reanudar sesión ----------

        /// <summary>
        /// Después de entrar (a mano o automáticamente): si el usuario tiene una sala en
        /// users/{uid}/current_room y sigue siendo miembro, regresa a ella:
        /// Room si el reto sigue (o espera al anfitrión), Final si ya terminó. Si no, al menú.
        /// </summary>
        async void ResumeOrGoToMenu()
        {
            var session = FirebaseLoginService.GetOrCreate();
            try
            {
                string code = await session.GetCurrentRoomAsync();
                if (string.IsNullOrEmpty(code))
                {
                    GoTo(AppScreen.Menu);
                    return;
                }

                var room = await FirebaseRoomService.GetRoomAsync(code);
                bool isMember = room != null && room.Props("users").Any(u => u.Name == session.Uid);
                if (!isMember)
                {
                    await session.ClearCurrentRoomAsync();
                    GoTo(AppScreen.Menu);
                    return;
                }

                // Room y Final leen el código de aquí.
                PlayerPrefs.SetString(CreateRoomController.RoomCodeKey, code);
                PlayerPrefs.Save();

                bool finished = room.Child("settings").Str("status") == FirebaseRoomService.StatusFinished;
                GoTo(finished ? AppScreen.Final : AppScreen.Room);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DetoxBet] No se pudo revisar la sala actual: {e.Message}");
                GoTo(AppScreen.Menu);
            }
        }

        // ---------- Escritorio / celular ----------

        void OnRootGeometryChanged(GeometryChangedEvent _) => UpdateLayoutMode();

        void UpdateLayoutMode()
        {
            float width = m_Document.rootVisualElement.resolvedStyle.width;
            if (float.IsNaN(width)) return;
            bool wide = width >= wideBreakpoint;
            if (wide == m_Wide) return;
            m_Wide = wide;
            ApplyLayoutMode();
        }

        void ApplyLayoutMode()
        {
            m_Frame?.EnableInClassList("app-frame--wide", m_Wide);
        }

        /// <summary>Lo llama JavaScript cuando la pestaña se oculta ("hidden") o vuelve ("visible").</summary>
        public void OnVisibilityChanged(string state)
        {
            m_Current?.OnApplicationPause(state == "hidden");
        }

        void OnEnable()
        {
            // Si el objeto se desactivó y reactivó, el UIDocument rehace su árbol:
            // se vuelve a mostrar la pantalla en la que estaba, un frame después.
            if (m_Started) StartCoroutine(ShowNextFrame(CurrentScreen));
        }

        IEnumerator ShowNextFrame(AppScreen screen)
        {
            yield return null;
            Show(screen);
        }

        void OnDisable()
        {
            m_Current?.OnHide();
        }

        /// <summary>
        /// Cambia de pantalla. Se aplica en el siguiente frame para no borrar
        /// el árbol a mitad de un clic.
        /// </summary>
        public void GoTo(AppScreen screen)
        {
            m_Document.rootVisualElement.schedule.Execute(() => Show(screen));
        }

        void Show(AppScreen screen)
        {
            if (!m_Screens.TryGetValue(screen, out var entry) || entry.uxml == null)
            {
                Debug.LogError($"[DetoxBet] Falta asignar el UXML de la pantalla {screen} en ScreenManager.");
                return;
            }

            m_Current?.OnHide();

            var root = m_Document.rootVisualElement;
            root.Clear();
            root.styleSheets.Clear();
            if (frameStyle != null) root.styleSheets.Add(frameStyle);
            if (responsiveStyle != null) root.styleSheets.Add(responsiveStyle);
            root.AddToClassList("app-root");
            root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

            // Todas las pantallas viven dentro de un marco centrado con ancho máximo.
            var frame = new VisualElement { name = "appFrame" };
            frame.AddToClassList("app-frame");
            root.Add(frame);
            m_Frame = frame;

            if (frameStyle == null)
            {
                // Respaldo: aunque falte AppFrame.uss, el marco siempre llena el alto de la pantalla.
                Debug.LogWarning("[DetoxBet] Asigna AppFrame.uss en el campo Frame Style del ScreenManager.");
                root.style.alignItems = Align.Center;
                frame.style.flexGrow = 1;
                frame.style.width = Length.Percent(100);
                frame.style.maxWidth = 440;
            }
            ApplyLayoutMode();
            entry.uxml.CloneTree(frame); // también agrega el USS que referencia el UXML

            CurrentScreen = screen;
            m_Current = entry.controller;
            m_Current.OnShow(frame);
        }

        void OnApplicationPause(bool paused)
        {
            m_Current?.OnApplicationPause(paused);
        }
    }
}
